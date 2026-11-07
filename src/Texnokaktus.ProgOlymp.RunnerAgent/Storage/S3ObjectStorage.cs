using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Texnokaktus.ProgOlymp.RunnerAgent.Diagnostics;

namespace Texnokaktus.ProgOlymp.RunnerAgent.Storage;

internal sealed class S3ObjectStorage(IAmazonS3 client, S3Options options) : IObjectStorage
{
    private readonly string _sourceBucket = options.SourceBucket;
    private readonly string _dataBucket = options.DataBucket;

    public async Task DownloadAsync(
        ObjectRole role,
        string key,
        ReadOnlyMemory<byte> sha256,
        string path,
        CancellationToken cancellationToken
    )
    {
        using var activity = RunnerTelemetry.Activities.StartActivity("runner.download");
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (sha256.Length != 32) throw new ArgumentException("SHA-256 must contain 32 bytes.");

        cancellationToken.ThrowIfCancellationRequested();

        var bucket = role switch
        {
            ObjectRole.Source => _sourceBucket, ObjectRole.Data => _dataBucket,
            _                 => throw new ArgumentOutOfRangeException(nameof(role))
        };

        var temporary = path + ".partial";

        try
        {
            await RetryAsync(
                async () =>
                {
                    await using var output = OpenLocal(temporary, FileMode.Create, FileAccess.Write);

                    using var response = await client.GetObjectAsync(
                                             new GetObjectRequest
                                             {
                                                 BucketName = bucket,
                                                 Key = key
                                             },
                                             cancellationToken
                                         );

                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[65536];
                    int count;

                    while ((count = await response.ResponseStream.ReadAsync(buffer, cancellationToken)) != 0)
                    {
                        hash.AppendData(buffer, 0, count);

                        try
                        {
                            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                        }
                        catch (IOException)
                        {
                            throw new ObjectStorageException("local_write_failed");
                        }
                    }

                    try
                    {
                        await output.FlushAsync(cancellationToken);
                    }
                    catch (IOException)
                    {
                        throw new ObjectStorageException("local_write_failed");
                    }

                    if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), sha256.Span))
                        throw new ObjectStorageException("hash_mismatch");

                    return true;
                },
                cancellationToken
            );

            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ObjectStorageException("local_write_failed");
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                /* The job's final workspace cleanup also owns this file. */
            }
        }
    }

    public async Task<ArtifactReference> UploadAsync(string path, string key, CancellationToken cancellationToken)
    {
        using var activity = RunnerTelemetry.Activities.StartActivity("runner.upload");
        ArgumentException.ThrowIfNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();
        byte[] hash;

        try
        {
            await using var input = OpenLocal(path, FileMode.Open, FileAccess.Read);
            hash = await SHA256.HashDataAsync(input, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ObjectStorageException("local_read_failed");
        }

        await RetryAsync(
            async () =>
            {
                await using var input = OpenLocal(path, FileMode.Open, FileAccess.Read);

                await client.PutObjectAsync(
                    new PutObjectRequest
                    {
                        BucketName = _dataBucket,
                        Key = key,
                        InputStream = input,
                        AutoCloseStream = false,
                        UseChunkEncoding = false
                    },
                    cancellationToken
                );

                return true;
            },
            cancellationToken
        );

        return new(key, hash);
    }

    private static FileStream OpenLocal(string path, FileMode mode, FileAccess access)
    {
        try
        {
            return new FileStream(
                path,
                mode,
                access,
                FileShare.Read,
                65536,
                FileOptions.Asynchronous | FileOptions.SequentialScan
            );
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ObjectStorageException(access == FileAccess.Read ? "local_read_failed" : "local_write_failed");
        }
    }

    private static async Task<T> RetryAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        for (var attempt = 0;; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            catch (ObjectStorageException)
            {
                RunnerTelemetry.StorageErrors.Add(1);

                throw;
            }
            catch (Exception exception) when (exception is AmazonServiceException
                                                        or AmazonClientException
                                                        or HttpRequestException
                                                        or UriFormatException
                                                        or IOException
                                                        or TimeoutException
                                                        or SocketException
                                                        or OperationCanceledException)
            {
                RunnerTelemetry.StorageErrors.Add(1);

                if (attempt >= 2 || exception is UriFormatException || !IsTransient(exception))
                    throw new ObjectStorageException(
                        exception is AmazonS3Exception { StatusCode: HttpStatusCode.NotFound }
                            ? "object_not_found"
                            : "storage_failed"
                    );
            }

            RunnerTelemetry.StorageRetries.Add(1);
            await Task.Delay(attempt == 0 ? 200 : 500, cancellationToken);
        }
    }

    private static bool IsTransient(Exception exception) => exception switch
    {
        AmazonServiceException service when service.ErrorCode == "SlowDown"  => true,
        AmazonServiceException service when service.StatusCode != 0          => RetryStatus(service.StatusCode),
        HttpRequestException { StatusCode: { } status }                      => RetryStatus(status),
        TimeoutException                                                     => true,
        HttpIOException { HttpRequestError: HttpRequestError.ResponseEnded } => true,
        HttpIOException                                                      => false,
        HttpRequestException request when request.HttpRequestError is not (HttpRequestError.Unknown
                                                                        or HttpRequestError.ConnectionError
                                                                        or HttpRequestError.ResponseEnded) => false,
        SocketException socket => socket.SocketErrorCode is SocketError.ConnectionReset
                                                         or SocketError.ConnectionAborted
                                                         or SocketError.NetworkReset
                                                         or SocketError.TimedOut
                                                         or SocketError.Shutdown
                                                         or SocketError.NotConnected,
        AmazonServiceException
         or AmazonClientException
         or IOException
         or HttpRequestException
         or OperationCanceledException =>
            exception.InnerException is not null && IsTransient(exception.InnerException),
        _ => false
    };

    private static bool RetryStatus(HttpStatusCode status) => status is HttpStatusCode.TooManyRequests
                                                                     or HttpStatusCode.InternalServerError
                                                                     or HttpStatusCode.BadGateway
                                                                     or HttpStatusCode.ServiceUnavailable
                                                                     or HttpStatusCode.GatewayTimeout;
}

using System.Net.Sockets;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using NSubstitute;
using Texnokaktus.ProgOlymp.RunnerAgent.Storage;

namespace Texnokaktus.ProgOlymp.RunnerAgent.Tests;

public sealed class StorageRetryTests
{
    [TestCase("client", 1)]
    [TestCase("status_zero", 1)]
    [TestCase("wrapped_status_zero", 3)]
    [TestCase("io", 1)]
    [TestCase("cancel", 1)]
    [TestCase("tls", 1)]
    [TestCase("dns", 1)]
    [TestCase("malformed", 1)]
    [TestCase("timeout", 3)]
    [TestCase("wrapped_timeout", 3)]
    [TestCase("reset", 3)]
    [TestCase("wrapped_reset", 3)]
    public async Task OnlyConfirmedTransportFailuresAreRetried(string kind, int attempts)
    {
        Exception exception = kind switch
        {
            "client"              => new AmazonClientException("private-client-failure"),
            "status_zero"         => new AmazonS3Exception("private-response") { StatusCode = 0 },
            "wrapped_status_zero" => new AmazonS3Exception("transport", new TimeoutException()) { StatusCode = 0 },
            "io"                  => new IOException("private-file-failure"),
            "cancel"              => new OperationCanceledException(),
            "tls" => new HttpRequestException(
                HttpRequestError.SecureConnectionError,
                "private-tls",
                new SocketException((int)SocketError.ConnectionReset)
            ),
            "dns"             => new HttpRequestException(HttpRequestError.NameResolutionError, "private-dns"),
            "malformed"       => new HttpIOException(HttpRequestError.InvalidResponse, "private-http"),
            "timeout"         => new TimeoutException(),
            "wrapped_timeout" => new TaskCanceledException("timeout", new TimeoutException()),
            "reset"           => new SocketException((int)SocketError.ConnectionReset),
            "wrapped_reset" => new AmazonClientException(
                "transport",
                new IOException("read", new SocketException((int)SocketError.ConnectionReset))
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        var client = Substitute.For<IAmazonS3>();

        client.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
              .Returns(_ => Task.FromException<GetObjectResponse>(exception));

        var directory = Directory.CreateTempSubdirectory("storage-retry-");

        try
        {
            var storage = new S3ObjectStorage(client, new S3Options());

            var error = Assert.ThrowsAsync<ObjectStorageException>(async () => await storage.DownloadAsync(
                                                                                   ObjectRole.Data,
                                                                                   "key",
                                                                                   new byte[32],
                                                                                   Path.Combine(
                                                                                       directory.FullName,
                                                                                       "input"
                                                                                   ),
                                                                                   CancellationToken.None
                                                                               )
            );

            Assert.That(error!.ToString(), Does.Not.Contain("private-"));

            Assert.That(
                client.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IAmazonS3.GetObjectAsync)),
                Is.EqualTo(attempts)
            );

            Assert.That(Directory.GetFiles(directory.FullName), Is.Empty);
        }
        finally
        {
            directory.Delete(true);
        }
    }
}

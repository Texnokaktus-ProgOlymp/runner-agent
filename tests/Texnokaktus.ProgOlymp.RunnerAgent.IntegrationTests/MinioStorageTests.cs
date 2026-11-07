using System.Security.Cryptography;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.DependencyInjection;
using Texnokaktus.ProgOlymp.RunnerAgent.IntegrationTests.Tools;
using Texnokaktus.ProgOlymp.RunnerAgent.Storage;

namespace Texnokaktus.ProgOlymp.RunnerAgent.IntegrationTests;

[Category("MinIO")]
public sealed class MinioStorageTests : SetupBase
{
    [Test]
    public async Task ProductionClientUsesSeparateBucketsAndRoundTripsArtifactsWithoutServerRegion()
    {
        var endpoint = Environment.GetEnvironmentVariable("RUNNER_TEST_MINIO_ENDPOINT");
        Assert.That(endpoint, Is.Not.Null.And.Not.Empty, "Use tests/prototype/run-storage.ps1.");
        var sourceBucket = "sources-" + Guid.NewGuid().ToString("N");
        var dataBucket = "data-" + Guid.NewGuid().ToString("N");

        await using var factory = new CustomWebApplicationFactory(
            "--S3:Endpoint",
            endpoint!,
            "--S3:SourceBucket",
            sourceBucket,
            "--S3:DataBucket",
            dataBucket
        );

        var client = factory.Services.GetRequiredService<IAmazonS3>();
        await client.PutBucketAsync(new PutBucketRequest { BucketName = sourceBucket });
        await client.PutBucketAsync(new PutBucketRequest { BucketName = dataBucket });
        var source = Encoding.UTF8.GetBytes("source-object");
        var input = Encoding.UTF8.GetBytes("input-object");
        await PutAsync(client, sourceBucket, source);
        await PutAsync(client, dataBucket, input);
        var directory = Directory.CreateTempSubdirectory("runner-minio-");

        try
        {
            var storage = factory.Services.GetRequiredService<IObjectStorage>();
            var sourcePath = Path.Combine(directory.FullName, "source");
            var inputPath = Path.Combine(directory.FullName, "input");

            await storage.DownloadAsync(
                ObjectRole.Source,
                "folder/object",
                SHA256.HashData(source),
                sourcePath,
                CancellationToken.None
            );

            await storage.DownloadAsync(
                ObjectRole.Data,
                "folder/object",
                SHA256.HashData(input),
                inputPath,
                CancellationToken.None
            );

            Assert.That(await File.ReadAllBytesAsync(sourcePath), Is.EqualTo(source));
            Assert.That(await File.ReadAllBytesAsync(inputPath), Is.EqualTo(input));

            var artifact = await storage.UploadAsync(
                               inputPath,
                               ObjectKeys.Artifact("run/", "tests/8/stdout.txt"),
                               CancellationToken.None
                           );

            Assert.That(artifact.Sha256, Is.EqualTo(SHA256.HashData(input)));
            using var response = await client.GetObjectAsync(dataBucket, artifact.Key);
            using var output = new MemoryStream();
            await response.ResponseStream.CopyToAsync(output);
            Assert.That(output.ToArray(), Is.EqualTo(input));

            var objects = await client.ListObjectsV2Async(
                              new ListObjectsV2Request
                              {
                                  BucketName = dataBucket,
                                  Prefix = "run/"
                              }
                          );

            Assert.That(objects.S3Objects.Select(item => item.Key), Is.EqualTo(new[] { artifact.Key }));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static async Task PutAsync(IAmazonS3 client, string bucket, byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);

        await client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = bucket,
                Key = "folder/object",
                InputStream = stream,
                UseChunkEncoding = false,
                AutoCloseStream = false
            }
        );
    }
}

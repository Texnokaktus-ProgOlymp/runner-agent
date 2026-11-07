using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Texnokaktus.ProgOlymp.RunnerAgent.IntegrationTests.Tools;
using Texnokaktus.ProgOlymp.RunnerAgent.Storage;

namespace Texnokaktus.ProgOlymp.RunnerAgent.IntegrationTests;

public sealed class StorageTests : SetupBase
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("verified-input");

    [Test]
    public async Task DownloadsByRoleAndVerifiesBytesWithoutUsingKeyAsLocalPath()
    {
        await using var server = await Backend.StartAsync(async (context, _) =>
                                     {
                                         Assert.That(
                                             context.Request.Path.Value,
                                             Is.EqualTo("/runner-sources/folder/input.cpp")
                                         );

                                         Assert.That(
                                             context.Request.Headers.Authorization.ToString(),
                                             Does.Contain("/us-east-1/s3/aws4_request")
                                         );

                                         await context.Response.Body.WriteAsync(Payload);
                                     }
                                 );

        await using var factory = new CustomWebApplicationFactory("--S3:Endpoint", server.Address);
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "chosen-by-agent");

        await factory.Services.GetRequiredService<IObjectStorage>()
                     .DownloadAsync(
                          ObjectRole.Source,
                          "folder/input.cpp",
                          SHA256.HashData(Payload),
                          path,
                          CancellationToken.None
                      );

        Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(Payload));
        Assert.That(server.Count, Is.EqualTo(1));
        Assert.That(File.Exists(path + ".partial"), Is.False);
    }

    [TestCase(404, 1)]
    [TestCase(403, 1)]
    [TestCase(503, 3)]
    public async Task UsesExactWireBudgetAndDoesNotExposeSdkMessages(int status, int expected)
    {
        await using var server = await Backend.StartAsync(async (context, _) =>
                                     {
                                         context.Response.StatusCode = status;
                                         context.Response.ContentType = "application/xml";

                                         await context.Response.WriteAsync(
                                             "<Error><Code>Failure</Code><Message>private-payload</Message></Error>"
                                         );
                                     }
                                 );

        await using var factory = new CustomWebApplicationFactory("--S3:Endpoint", server.Address);
        using var directory = new TestDirectory();

        var exception = Assert.ThrowsAsync<ObjectStorageException>(async () =>
                                                                       await factory.Services
                                                                                    .GetRequiredService<
                                                                                         IObjectStorage>()
                                                                                    .DownloadAsync(
                                                                                         ObjectRole.Data,
                                                                                         "sensitive-key",
                                                                                         SHA256.HashData(Payload),
                                                                                         Path.Combine(
                                                                                             directory.Path,
                                                                                             "input"
                                                                                         ),
                                                                                         CancellationToken.None
                                                                                     )
        );

        Assert.That(exception!.ToString(), Does.Not.Contain("private-payload").And.Not.Contain("sensitive-key"));
        Assert.That(server.Count, Is.EqualTo(expected));

        if (expected == 3)
        {
            Assert.That(server.Times[1] - server.Times[0], Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(180)));
            Assert.That(server.Times[2] - server.Times[1], Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(480)));
        }
    }

    [Test]
    public async Task HashMismatchIsPermanentAndLeavesNoUsableFile()
    {
        await using var server =
            await Backend.StartAsync(async (context, _) => await context.Response.Body.WriteAsync(Payload));

        await using var factory = new CustomWebApplicationFactory("--S3:Endpoint", server.Address);
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "input");

        var exception = Assert.ThrowsAsync<ObjectStorageException>(async () =>
                                                                       await factory.Services
                                                                                    .GetRequiredService<
                                                                                         IObjectStorage>()
                                                                                    .DownloadAsync(
                                                                                         ObjectRole.Data,
                                                                                         "input",
                                                                                         new byte[32],
                                                                                         path,
                                                                                         CancellationToken.None
                                                                                     )
        );

        Assert.That(exception!.Code, Is.EqualTo("hash_mismatch"));
        Assert.That(server.Count, Is.EqualTo(1));
        Assert.That(File.Exists(path), Is.False);
        Assert.That(File.Exists(path + ".partial"), Is.False);
    }

    [Test]
    public async Task RestartsInterruptedDownloadFromBeginning()
    {
        await using var server = await Backend.StartAsync(async (context, count) =>
                                     {
                                         Assert.That(context.Request.Headers.Range.ToString(), Is.Empty);
                                         context.Response.ContentLength = Payload.Length;

                                         if (count == 1)
                                         {
                                             await context.Response.Body.WriteAsync(Payload.AsMemory(0, 3));
                                             await context.Response.Body.FlushAsync();
                                             context.Abort();
                                         }
                                         else await context.Response.Body.WriteAsync(Payload);
                                     }
                                 );

        await using var factory = new CustomWebApplicationFactory("--S3:Endpoint", server.Address);
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "input");

        await factory.Services.GetRequiredService<IObjectStorage>()
                     .DownloadAsync(
                          ObjectRole.Data,
                          "input",
                          SHA256.HashData(Payload),
                          path,
                          CancellationToken.None
                      );

        Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(Payload));
        Assert.That(server.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task UploadRetriesSameBytesAndReturnsTheirHash()
    {
        await using var server = await Backend.StartAsync(async (context, count) =>
                                     {
                                         Assert.That(context.Request.Method, Is.EqualTo("PUT"));

                                         Assert.That(
                                             context.Request.Path.Value,
                                             Is.EqualTo("/runner-data/run/tests/8/stdout.txt")
                                         );

                                         using var bytes = new MemoryStream();
                                         await context.Request.Body.CopyToAsync(bytes);
                                         Assert.That(bytes.ToArray(), Is.EqualTo(Payload));
                                         context.Response.StatusCode = count == 1 ? 503 : 200;
                                         context.Response.Headers.ETag = "\"test\"";
                                     }
                                 );

        await using var factory = new CustomWebApplicationFactory("--S3:Endpoint", server.Address);
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "stdout");
        await File.WriteAllBytesAsync(path, Payload);

        var artifact = await factory.Services.GetRequiredService<IObjectStorage>()
                                    .UploadAsync(
                                         path,
                                         ObjectKeys.Artifact("run/", "tests/8/stdout.txt"),
                                         CancellationToken.None
                                     );

        Assert.That(artifact.Sha256, Is.EqualTo(SHA256.HashData(Payload)));
        Assert.That(artifact.Key, Is.EqualTo("run/tests/8/stdout.txt"));
        Assert.That(server.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task CancellationStopsRetries()
    {
        using var cancellation = new CancellationTokenSource();

        await using var server = await Backend.StartAsync((context, _) =>
                                     {
                                         context.Response.StatusCode = 503;
                                         cancellation.Cancel();

                                         return Task.CompletedTask;
                                     }
                                 );

        await using var factory = new CustomWebApplicationFactory("--S3:Endpoint", server.Address);
        using var directory = new TestDirectory();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
                                                           await factory.Services.GetRequiredService<IObjectStorage>()
                                                                        .DownloadAsync(
                                                                             ObjectRole.Data,
                                                                             "input",
                                                                             SHA256.HashData(Payload),
                                                                             Path.Combine(directory.Path, "input"),
                                                                             cancellation.Token
                                                                         )
        );

        Assert.That(server.Count, Is.EqualTo(1));
    }

    private sealed class TestDirectory : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("runner-storage-");
        public string Path => _directory.FullName;
        public void Dispose() => _directory.Delete(true);
    }

    private sealed class Backend(WebApplication application) : IAsyncDisposable
    {
        private int _count;
        public int Count => _count;
        public List<TimeSpan> Times { get; } = [];
        public string Address => application.Urls.Single();

        public static async Task<Backend> StartAsync(Func<HttpContext, int, Task> handle)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();

            builder.WebHost.UseUrls("http://127.0.0.1:0")
                   .ConfigureKestrel(options =>
                                         options.ConfigureEndpointDefaults(endpoint => endpoint.Protocols =
                                                                                           HttpProtocols.Http1
                                         )
                    );

            var app = builder.Build();
            var backend = new Backend(app);
            var clock = Stopwatch.StartNew();

            app.Run(async context =>
                {
                    var count = Interlocked.Increment(ref backend._count);
                    backend.Times.Add(clock.Elapsed);
                    await handle(context, count);
                }
            );

            await app.StartAsync();

            return backend;
        }

        public ValueTask DisposeAsync() => application.DisposeAsync();
    }
}

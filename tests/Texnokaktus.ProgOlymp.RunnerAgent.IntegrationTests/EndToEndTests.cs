using System.Diagnostics;
using System.Formats.Tar;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Health.V1;
using Grpc.Net.Client;
using Wire = Texnokaktus.ProgOlymp.RunnerAgent.Contracts;

namespace Texnokaktus.ProgOlymp.RunnerAgent.IntegrationTests;

[Category("EndToEnd"), NonParallelizable]
public sealed class EndToEndTests : IDisposable
{
    private GrpcChannel _channel = null!;
    private Wire.Runner.RunnerClient _runner = null!;
    private Health.HealthClient _health = null!;
    private AmazonS3Client _storage = null!;
    private string _container = null!;
    private static readonly int[] Ids = [8, 2, 5];

    [OneTimeSetUp]
    public async Task Start()
    {
        var endpoint = Environment.GetEnvironmentVariable("RUNNER_TEST_AGENT_ENDPOINT");
        var storage = Environment.GetEnvironmentVariable("RUNNER_TEST_MINIO_ENDPOINT");
        _container = Environment.GetEnvironmentVariable("RUNNER_TEST_AGENT_CONTAINER")!;
        Assert.That(endpoint, Is.Not.Null.And.Not.Empty, "Use tests/prototype/run-e2e.ps1.");
        Assert.That(storage, Is.Not.Null.And.Not.Empty);
        Assert.That(_container, Is.Not.Null.And.Not.Empty);
        _channel = GrpcChannel.ForAddress(endpoint!, new GrpcChannelOptions { MaxReceiveMessageSize = null });
        _runner = new(_channel);
        _health = new(_channel);

        _storage = new AmazonS3Client(
            new BasicAWSCredentials("runner-test-access", "runner-test-secret"),
            new AmazonS3Config
            {
                ServiceURL = storage,
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1",
                MaxErrorRetry = 0
            }
        );

        await _storage.PutBucketAsync(new PutBucketRequest { BucketName = "runner-sources" });
        await _storage.PutBucketAsync(new PutBucketRequest { BucketName = "runner-data" });
        var timeout = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                await _health.CheckAsync(new HealthCheckRequest(), deadline: DateTime.UtcNow.AddSeconds(2));

                break;
            }
            catch (RpcException) when (timeout.Elapsed < TimeSpan.FromSeconds(40))
            {
                await Task.Delay(100);
            }
        }
    }

    [Test, Order(1)]
    public async Task ExecutesRealCppAndPublishesVerifiedOutputThenReusesSameId()
    {
        const string source = """
                              #include <iostream>
                              #include <fstream>
                              #ifndef ONLINE_JUDGE
                              #error ONLINE_JUDGE is required
                              #endif
                              int main(){if(std::ifstream("/box/marker").good())return 90;std::ofstream("/box/marker")<<1;
                              int n;std::cin>>n;std::cout<<n*2;std::cerr<<"err"<<n;return n==2?3:0;}
                              """;

        var request = await PrepareAsync(123, source);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            request.ResultPrefix = "run-" + Guid.NewGuid().ToString("N");
            var result = await _runner.RunJobAsync(request);
            Assert.That(result.Status, Is.EqualTo(Wire.JobStatus.Completed));
            Assert.That(result.Tests.Select(test => test.TestId), Is.EqualTo(Ids));

            foreach (var test in result.Tests)
            {
                Assert.That(
                    await ReadArtifactAsync(test.Execution.Stdout),
                    Is.EqualTo(
                        Encoding.UTF8.GetBytes(
                            (test.TestId * 2).ToString(System.Globalization.CultureInfo.InvariantCulture)
                        )
                    )
                );

                Assert.That(
                    test.Execution.HasCpuTimeMs && test.Execution.HasWallTimeMs && test.Execution.HasPeakMemoryBytes,
                    Is.True
                );

                Assert.That(test.Execution.AppliedMemoryLimitBytes % 4096, Is.Zero);
            }

            Assert.That(result.Tests[1].Execution.Reason, Is.EqualTo(Wire.TerminationReason.RuntimeError));

            Assert.That(
                await DockerAsync(
                    "exec",
                    _container,
                    "sh",
                    "-c",
                    "find /var/lib/runner-agent/runner-agent-jobs -mindepth 1 -maxdepth 1 -type d"
                ),
                Is.Empty
            );
        }
    }

    [Test, Order(2)]
    public async Task BadHashAndCompilationErrorRemainStructuredGrpcSuccess()
    {
        var request = await PrepareAsync(124, "#error expected compiler failure");
        request.Source.Sha256 = ByteString.CopyFrom(new byte[32]);
        var badHash = await _runner.RunJobAsync(request);
        Assert.That(badHash.Status, Is.EqualTo(Wire.JobStatus.InfrastructureError));
        Assert.That(badHash.Compilation, Is.Null);
        Assert.That(badHash.Tests, Is.Empty);
        request = await PrepareAsync(124, "#error expected compiler failure");
        var compiler = await _runner.RunJobAsync(request);
        Assert.That(compiler.Status, Is.EqualTo(Wire.JobStatus.CompilationError));
        Assert.That(compiler.Tests, Is.Empty);
        Assert.That((await ReadArtifactAsync(compiler.Compilation.Stderr)).Length, Is.GreaterThan(0));
    }

    [Test, Order(3)]
    public async Task BadInputDoesNotExecuteAndOtherTestsContinue()
    {
        var request = await PrepareAsync(130, "int main(){}");
        request.Tests[1].Input.Sha256 = ByteString.CopyFrom(new byte[32]);
        var result = await _runner.RunJobAsync(request);
        Assert.That(result.Status, Is.EqualTo(Wire.JobStatus.Completed));
        Assert.That(result.Tests.Select(test => test.TestId), Is.EqualTo(Ids));
        Assert.That(result.Tests[1].Status, Is.EqualTo(Wire.TestStatus.InfrastructureError));
        Assert.That(result.Tests[1].Execution, Is.Null);
        Assert.That(result.Tests[2].Execution.Reason, Is.EqualTo(Wire.TerminationReason.Success));
    }

    [TestCase(false), Order(3)]
    [TestCase(true)]
    public async Task CancelsRealProcessAndReusesWorkerAfterCleanup(bool deadline)
    {
        var request = await PrepareAsync(125, "#include <unistd.h>\nint main(){sleep(30);}");
        request.ExecutionLimits.WallTimeMs = 60000;
        using var cancellation = new CancellationTokenSource();

        using var call = _runner.RunJobAsync(
            request,
            deadline: deadline ? DateTime.UtcNow.AddSeconds(3) : null,
            cancellationToken: cancellation.Token
        );

        await WaitForTestAsync();

        Assert.That(
            (await _health.CheckAsync(new HealthCheckRequest())).Status,
            Is.EqualTo(HealthCheckResponse.Types.ServingStatus.Serving)
        );

        var busy = Assert.ThrowsAsync<RpcException>(async () => await _runner.RunJobAsync(new Wire.RunJobRequest()));
        Assert.That(busy!.StatusCode, Is.EqualTo(StatusCode.ResourceExhausted));
        if (!deadline) await cancellation.CancelAsync();
        var canceled = Assert.ThrowsAsync<RpcException>(async () => await call.ResponseAsync);
        Assert.That(canceled!.StatusCode, Is.EqualTo(deadline ? StatusCode.DeadlineExceeded : StatusCode.Cancelled));
        var next = await PrepareAsync(125, "int main(){}");
        var timeout = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                Assert.That((await _runner.RunJobAsync(next)).Status, Is.EqualTo(Wire.JobStatus.Completed));

                break;
            }
            catch (RpcException exception) when (exception.StatusCode == StatusCode.ResourceExhausted
                                              && timeout.Elapsed < TimeSpan.FromSeconds(20))
            {
                await Task.Delay(50);
            }
        }
    }

    [Test, Order(4)]
    public async Task DisconnectedClientStopsExecutionAndWorkerCanAcceptAnotherCall()
    {
        Socket? socket = null;

        using var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (context, token) =>
            {
                var connection = new Socket(SocketType.Stream, ProtocolType.Tcp);
                await connection.ConnectAsync(context.DnsEndPoint, token);
                socket = connection;

                return new NetworkStream(connection, ownsSocket: true);
            }
        };

        var endpoint = "http://" + await DockerAsync("port", _container, "8080/tcp");
        using var channel = GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions { HttpHandler = handler });
        var client = new Wire.Runner.RunnerClient(channel);
        var request = await PrepareAsync(136, "#include <unistd.h>\nint main(){sleep(30);}");
        request.ExecutionLimits.WallTimeMs = 60000;
        using var call = client.RunJobAsync(request);
        await WaitForTestAsync();
        Assert.That(socket, Is.Not.Null);
        socket!.Dispose();
        Assert.ThrowsAsync<RpcException>(async () => await call.ResponseAsync.WaitAsync(TimeSpan.FromSeconds(10)));
        var next = await PrepareAsync(136, "int main(){}");
        var timeout = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                Assert.That((await _runner.RunJobAsync(next)).Status, Is.EqualTo(Wire.JobStatus.Completed));

                break;
            }
            catch (RpcException exception) when (exception.StatusCode == StatusCode.ResourceExhausted
                                              && timeout.Elapsed < TimeSpan.FromSeconds(20))
            {
                await Task.Delay(50);
            }
        }
    }

    [Test, Order(4)]
    public async Task SigkillRecoveryRemovesOwnOrphansAndPreservesForeignFiles()
    {
        var request = await PrepareAsync(127, "#include <unistd.h>\nint main(){sleep(30);}");
        request.ExecutionLimits.WallTimeMs = 60000;
        using var call = _runner.RunJobAsync(request);
        await WaitForTestAsync();
        await DockerAsync("exec", _container, "sh", "-c", "printf preserve > /var/lib/runner-agent/unrelated");
        await DockerAsync("kill", "--signal=KILL", _container);
        Assert.ThrowsAsync<RpcException>(async () => await call.ResponseAsync);
        Assert.That(await DockerAsync("wait", _container), Is.EqualTo("137"));
        await RestartAsync();

        Assert.That(
            await DockerAsync("exec", _container, "cat", "/var/lib/runner-agent/unrelated"),
            Is.EqualTo("preserve")
        );

        Assert.That(
            await DockerAsync(
                "exec",
                _container,
                "sh",
                "-c",
                "find /var/lib/runner-agent/runner-agent-jobs -mindepth 1 -maxdepth 1 -type d"
            ),
            Is.Empty
        );

        Assert.That(
            (await _runner.RunJobAsync(await PrepareAsync(127, "int main(){}"))).Status,
            Is.EqualTo(Wire.JobStatus.Completed)
        );
    }

    [TestCase(8, Wire.JobStatus.InfrastructureError, 1), Order(5)]
    [TestCase(5, Wire.JobStatus.Completed, 3)]
    public async Task FatalCleanupReturnsKnownResultsAndExitsOne(int failedTest, Wire.JobStatus status, int count)
    {
        var request = await PrepareAsync(128, "#include <unistd.h>\nint main(){sleep(1);}");
        using var call = _runner.RunJobAsync(request);
        await WaitForTestAsync(failedTest);
        await DockerAsync("exec", _container, "mv", "/usr/bin/isolate", "/usr/bin/isolate.cleanup-test");
        var result = await call.ResponseAsync;
        Assert.That(result.Status, Is.EqualTo(status));
        Assert.That(result.Tests, Has.Count.EqualTo(count));
        Assert.That(result.Tests[^1].Execution.Reason, Is.EqualTo(Wire.TerminationReason.Success));
        Assert.That(result.Tests[^1].Status, Is.EqualTo(Wire.TestStatus.InfrastructureError));
        Assert.That(await DockerAsync("wait", _container), Is.EqualTo("1"));
        var saved = Path.Combine(Path.GetTempPath(), "runner-isolate-" + Guid.NewGuid().ToString("N"));

        try
        {
            await DockerAsync("cp", _container + ":/usr/bin/isolate.cleanup-test", saved);
            await RestoreExecutableAsync(saved);
        }
        finally
        {
            File.Delete(saved);
        }

        await RestartAsync();
    }

    [Test, Order(8)]
    public async Task StartsAndServesHealthWhileStorageIsUnavailable()
    {
        var request = await PrepareAsync(133, "int main(){}");
        var minio = Environment.GetEnvironmentVariable("RUNNER_TEST_MINIO_CONTAINER")!;
        Assert.That(minio, Is.Not.Null.And.Not.Empty);
        await DockerAsync("stop", minio);

        try
        {
            await DockerAsync("kill", "--signal=TERM", _container);
            Assert.That(await DockerAsync("wait", _container), Is.EqualTo("0"));
            await RestartAsync();
            Assert.That((await _runner.RunJobAsync(request)).Status, Is.EqualTo(Wire.JobStatus.InfrastructureError));

            Assert.That(
                (await _health.CheckAsync(new HealthCheckRequest())).Status,
                Is.EqualTo(HealthCheckResponse.Types.ServingStatus.Serving)
            );
        }
        finally
        {
            await DockerAsync("start", minio);
            var endpoint = "http://" + await DockerAsync("port", minio, "9000/tcp");
            _storage.Dispose();

            _storage = new AmazonS3Client(
                new BasicAWSCredentials("runner-test-access", "runner-test-secret"),
                new AmazonS3Config
                {
                    ServiceURL = endpoint,
                    ForcePathStyle = true,
                    AuthenticationRegion = "us-east-1",
                    MaxErrorRetry = 0
                }
            );

            var timeout = Stopwatch.StartNew();

            while (true)
            {
                try
                {
                    await _storage.ListBucketsAsync();

                    break;
                }
                catch (Exception exception) when (exception is AmazonS3Exception or HttpRequestException or IOException
                                               && timeout.Elapsed < TimeSpan.FromSeconds(30))
                {
                    await Task.Delay(100);
                }
            }
        }
    }

    [Test, Order(9)]
    public async Task SigtermDrainsJobKeepsHealthServingAndExitsZero()
    {
        var request = await PrepareAsync(126, "#include <unistd.h>\nint main(){sleep(2);}");
        request.ExecutionLimits.WallTimeMs = 5000;
        using var call = _runner.RunJobAsync(request);
        await WaitForTestAsync();
        await DockerAsync("kill", "--signal=TERM", _container);

        var rejected =
            Assert.ThrowsAsync<RpcException>(async () => await _runner.RunJobAsync(new Wire.RunJobRequest()));

        Assert.That(rejected!.StatusCode, Is.EqualTo(StatusCode.Unavailable));

        Assert.That(
            (await _health.CheckAsync(new HealthCheckRequest())).Status,
            Is.EqualTo(HealthCheckResponse.Types.ServingStatus.Serving)
        );

        Assert.That((await call.ResponseAsync).Status, Is.EqualTo(Wire.JobStatus.Completed));
        Assert.That(await DockerAsync("wait", _container), Is.EqualTo("0"));
    }

    [Test, Order(10)]
    public async Task CancellationDuringShutdownStillCleansAndExitsZero()
    {
        await RestartAsync();
        var request = await PrepareAsync(131, "#include <unistd.h>\nint main(){sleep(30);}");
        request.ExecutionLimits.WallTimeMs = 60000;
        using var cancellation = new CancellationTokenSource();
        using var call = _runner.RunJobAsync(request, cancellationToken: cancellation.Token);
        await WaitForTestAsync();
        await DockerAsync("kill", "--signal=TERM", _container);
        await cancellation.CancelAsync();
        Assert.ThrowsAsync<RpcException>(async () => await call.ResponseAsync);
        Assert.That(await DockerAsync("wait", _container), Is.EqualTo("0"));
    }

    [Test, Order(11)]
    public async Task CancellationWithFailedCleanupExitsOne()
    {
        await RestartAsync();
        var request = await PrepareAsync(132, "#include <unistd.h>\nint main(){sleep(30);}");
        request.ExecutionLimits.WallTimeMs = 60000;
        using var cancellation = new CancellationTokenSource();
        using var call = _runner.RunJobAsync(request, cancellationToken: cancellation.Token);
        await WaitForTestAsync();
        await DockerAsync("exec", _container, "mv", "/usr/bin/isolate", "/usr/bin/isolate.cancel-test");
        await cancellation.CancelAsync();
        Assert.ThrowsAsync<RpcException>(async () => await call.ResponseAsync);
        Assert.That(await DockerAsync("wait", _container), Is.EqualTo("1"));
    }

    private async Task RestartAsync()
    {
        await DockerAsync("start", _container);
        var endpoint = "http://" + await DockerAsync("port", _container, "8080/tcp");
        _channel.Dispose();
        _channel = GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions { MaxReceiveMessageSize = null });
        _runner = new(_channel);
        _health = new(_channel);
        var timeout = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                await _health.CheckAsync(new HealthCheckRequest(), deadline: DateTime.UtcNow.AddSeconds(2));

                return;
            }
            catch (RpcException) when (timeout.Elapsed < TimeSpan.FromSeconds(40))
            {
                await Task.Delay(100);
            }
        }
    }

    private async Task RestoreExecutableAsync(string path)
    {
        using var archive = new MemoryStream();

        using (var writer = new TarWriter(archive, leaveOpen: true))
        using (var binary = File.OpenRead(path))
        {
            writer.WriteEntry(
                new PaxTarEntry(TarEntryType.RegularFile, "isolate")
                {
                    Mode = (UnixFileMode)0x1ED,
                    DataStream = binary
                }
            );
        }

        archive.Position = 0;

        var start = new ProcessStartInfo("docker")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        start.ArgumentList.Add("cp");
        start.ArgumentList.Add("-");
        start.ArgumentList.Add(_container + ":/usr/bin");
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await archive.CopyToAsync(process.StandardInput.BaseStream);
        process.StandardInput.Close();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40));
        Assert.That(process.ExitCode, Is.Zero, await error);
        await output;
    }

    private async Task WaitForTestAsync(int testId = 8)
    {
        var timeout = Stopwatch.StartNew();

        while (true)
        {
            var observed = await DockerAsync(
                               "exec",
                               _container,
                               "sh",
                               "-c",
                               $"find /var/lib/runner-agent/runner-agent-jobs -path '*/tests/{testId}/isolate.meta' -print -quit"
                           );

            if (observed.Length != 0) return;

            if (timeout.Elapsed > TimeSpan.FromSeconds(30)) Assert.Fail("The execution stage did not start.");
            await Task.Delay(50);
        }
    }

    private async Task<Wire.RunJobRequest> PrepareAsync(long id, string source)
    {
        var key = Guid.NewGuid().ToString("N");
        var bytes = Encoding.UTF8.GetBytes(source);
        await PutAsync("runner-sources", key, bytes);

        var request = new Wire.RunJobRequest
        {
            JobId = id,
            Language = "cpp",
            LanguageVersion = "23",
            Source = new()
            {
                Key = key,
                Sha256 = ByteString.CopyFrom(SHA256.HashData(bytes))
            },
            ResultPrefix = "run-" + key,
            CompilationLimits = new()
            {
                CpuTimeMs = 10000,
                WallTimeMs = 20000,
                MemoryBytes = 512 * 1024 * 1024,
                StdoutBytes = 1024 * 1024,
                StderrBytes = 1024 * 1024,
                Processes = 32
            },
            ExecutionLimits = new()
            {
                CpuTimeMs = 1000,
                WallTimeMs = 2000,
                MemoryBytes = 64 * 1024 * 1024 + 1,
                StdoutBytes = 1024,
                StderrBytes = 1024,
                Processes = 8
            }
        };

        foreach (var testId in Ids)
        {
            var input = Encoding.UTF8.GetBytes(testId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var inputKey = key + "/" + testId;
            await PutAsync("runner-data", inputKey, input);

            request.Tests.Add(
                new Wire.JobTest
                {
                    TestId = testId,
                    Input = new()
                    {
                        Key = inputKey,
                        Sha256 = ByteString.CopyFrom(SHA256.HashData(input))
                    }
                }
            );
        }

        return request;
    }

    private async Task PutAsync(string bucket, string key, byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);

        await _storage.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = bucket,
                Key = key,
                InputStream = stream,
                AutoCloseStream = false,
                UseChunkEncoding = false
            }
        );
    }

    private async Task<byte[]> ReadArtifactAsync(Wire.ObjectReference reference)
    {
        using var response = await _storage.GetObjectAsync("runner-data", reference.Key);
        using var bytes = new MemoryStream();
        await response.ResponseStream.CopyToAsync(bytes);
        var payload = bytes.ToArray();
        Assert.That(reference.Sha256.ToByteArray(), Is.EqualTo(SHA256.HashData(payload)));

        return payload;
    }

    private static async Task<string> DockerAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40));
        Assert.That(process.ExitCode, Is.Zero, await error);

        return (await output).Trim();
    }

    [OneTimeTearDown]
    public void Dispose()
    {
        _channel?.Dispose();
        _storage?.Dispose();
    }
}

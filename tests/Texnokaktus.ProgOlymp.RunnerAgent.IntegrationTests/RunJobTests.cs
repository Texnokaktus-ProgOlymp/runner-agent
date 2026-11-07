using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Texnokaktus.ProgOlymp.RunnerAgent.Execution;
using Texnokaktus.ProgOlymp.RunnerAgent.IntegrationTests.Tools;
using Texnokaktus.ProgOlymp.RunnerAgent.Storage;
using Wire = Texnokaktus.ProgOlymp.RunnerAgent.Contracts;

namespace Texnokaktus.ProgOlymp.RunnerAgent.IntegrationTests;

public sealed class RunJobTests : SetupBase
{
    private static readonly int[] Ids = [8, 2, 5];

    [Test]
    public async Task ReturnsOrderedResultsAfterPublicationAndCleanupAndAllowsRepeatedId()
    {
        await using var app = new TestApplication();
        var request = Request();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var response = await app.Client.RunJobAsync(request);

            Assert.Multiple(() =>
                {
                    Assert.That(response.JobId, Is.EqualTo(long.MaxValue));
                    Assert.That(response.Status, Is.EqualTo(Wire.JobStatus.Completed));
                    Assert.That(response.Tests.Select(test => test.TestId), Is.EqualTo(Ids));
                    Assert.That(response.Compilation.HasExitCode, Is.True);
                    Assert.That(response.Compilation.ExitCode, Is.Zero);
                    Assert.That(response.Compilation.HasSignal, Is.False);
                    Assert.That(response.Tests[1].Execution.Reason, Is.EqualTo(Wire.TerminationReason.RuntimeError));
                    Assert.That(response.Tests[1].Status, Is.EqualTo(Wire.TestStatus.Completed));
                    Assert.That(response.Tests[0].Execution.Stdout.Sha256.Length, Is.EqualTo(32));
                    Assert.That(Directory.Exists(app.Runner.LastWorkspace), Is.False);
                }
            );
        }

        Assert.That(app.Runner.Calls, Is.EqualTo(2));
        Assert.That(app.Storage.Uploads, Has.Count.EqualTo(16));
    }

    [TestCase("id")]
    [TestCase("language")]
    [TestCase("hash")]
    [TestCase("duplicate")]
    [TestCase("tests")]
    [TestCase("limits")]
    [TestCase("overflow")]
    [TestCase("timer")]
    [TestCase("test_id")]
    [TestCase("prefix")]
    [TestCase("key")]
    [TestCase("version")]
    public async Task InvalidRequestDoesNotDownloadOrExecute(string field)
    {
        await using var app = new TestApplication();
        var request = Request();

        switch (field)
        {
            case "id":        request.JobId = 0; break;
            case "language":  request.Language = "CPP"; break;
            case "hash":      request.Source.Sha256 = ByteString.CopyFrom(new byte[31]); break;
            case "duplicate": request.Tests[1].TestId = 8; break;
            case "tests":     request.Tests.Clear(); break;
            case "limits":    request.ExecutionLimits = null; break;
            case "overflow":  request.ExecutionLimits.MemoryBytes = long.MaxValue; break;
            case "timer":     request.ExecutionLimits.CpuTimeMs = (long)int.MaxValue + 1; break;
            case "test_id":   request.Tests[0].TestId = 0; break;
            case "prefix":    request.ResultPrefix = ""; break;
            case "key":       request.Tests[0].Input.Key = ""; break;
            case "version":   request.LanguageVersion = "c++23"; break;
        }

        var exception = Assert.ThrowsAsync<RpcException>(async () => await app.Client.RunJobAsync(request));
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
        Assert.That(app.Storage.Downloads, Is.Zero);
        Assert.That(app.Runner.Calls, Is.Zero);
    }

    [Test]
    public async Task BusyAdmissionPrecedesBusinessValidationWithoutComparingIds()
    {
        await using var app = new TestApplication();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        app.Runner.BeforeRun = async token =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(token);
        };

        using var first = app.Client.RunJobAsync(Request());

        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var health = await app.Health.CheckAsync(new Grpc.Health.V1.HealthCheckRequest());
            Assert.That(health.Status, Is.EqualTo(Grpc.Health.V1.HealthCheckResponse.Types.ServingStatus.Serving));

            var malformed = new Wire.RunJobRequest
            {
                JobId = long.MaxValue,
                Language = "unsupported"
            };

            using var rejected = new CancellationTokenSource();

            var exception =
                Assert.ThrowsAsync<RpcException>(async () => await app.Client.RunJobAsync(
                                                                 malformed,
                                                                 cancellationToken: rejected.Token
                                                             )
                );

            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCode.ResourceExhausted));
            await rejected.CancelAsync();
            Assert.That(first.ResponseAsync.IsCompleted, Is.False);
            Assert.That(app.Storage.Downloads, Is.EqualTo(4));
            Assert.That(app.Runner.Calls, Is.EqualTo(1));
        }
        finally
        {
            release.TrySetResult();
            await first.ResponseAsync;
        }
    }

    [Test]
    public async Task PublicationFailurePreservesVerdictAndOtherLinks()
    {
        await using var app = new TestApplication();
        app.Storage.FailedUpload = "tests/2/stdout.txt";
        var result = await app.Client.RunJobAsync(Request());
        Assert.That(result.Status, Is.EqualTo(Wire.JobStatus.Completed));
        Assert.That(result.Tests[1].Status, Is.EqualTo(Wire.TestStatus.InfrastructureError));
        Assert.That(result.Tests[1].Execution.Reason, Is.EqualTo(Wire.TerminationReason.RuntimeError));
        Assert.That(result.Tests[1].Execution.ExitCode, Is.EqualTo(3));
        Assert.That(result.Tests[1].Execution.Stdout, Is.Null);
        Assert.That(result.Tests[1].Execution.Stderr, Is.Not.Null);
        Assert.That(result.Tests[2].Execution.Stdout, Is.Not.Null);
    }

    [Test]
    public async Task SourceFailureHasNoCompilationOrFakeTests()
    {
        await using var app = new TestApplication();
        app.Storage.FailSource = true;
        var result = await app.Client.RunJobAsync(Request());
        Assert.That(result.Status, Is.EqualTo(Wire.JobStatus.InfrastructureError));
        Assert.That(result.Compilation, Is.Null);
        Assert.That(result.Tests, Is.Empty);
        Assert.That(app.Runner.Calls, Is.Zero);
        Assert.That(app.Storage.Downloads, Is.EqualTo(1));
    }

    [Test]
    public async Task CompilerPublicationFailurePreservesSuccessfulCompilationAndAllTests()
    {
        await using var app = new TestApplication();
        app.Storage.FailedUpload = "compilation/stdout.txt";
        var result = await app.Client.RunJobAsync(Request());
        Assert.That(result.Status, Is.EqualTo(Wire.JobStatus.Completed));
        Assert.That(result.Compilation.Reason, Is.EqualTo(Wire.TerminationReason.Success));
        Assert.That(result.Compilation.Stdout, Is.Null);
        Assert.That(result.Compilation.Stderr, Is.Not.Null);
        Assert.That(result.Diagnostics, Is.Not.Empty);
        Assert.That(result.Tests, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task CancellationKeepsSlotUntilIndependentCleanupFinishes()
    {
        await using var app = new TestApplication();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        app.Runner.BeforeRun = async token =>
        {
            started.SetResult();

            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (OperationCanceledException)
            {
                canceled.SetResult();
                await cleanup.Task;

                throw;
            }
        };

        using var cancellation = new CancellationTokenSource();
        using var call = app.Client.RunJobAsync(Request(), cancellationToken: cancellation.Token);

        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await cancellation.CancelAsync();
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var rejected = Assert.ThrowsAsync<RpcException>(async () => await app.Client.RunJobAsync(Request()));
            Assert.That(rejected!.StatusCode, Is.EqualTo(StatusCode.ResourceExhausted));
            Assert.That(Directory.Exists(app.Runner.LastWorkspace), Is.True);
        }
        finally
        {
            cleanup.TrySetResult();
        }

        Assert.ThrowsAsync<RpcException>(async () => await call.ResponseAsync);
        app.Runner.BeforeRun = null;
        var timeout = System.Diagnostics.Stopwatch.StartNew();

        while (true)
        {
            try
            {
                await app.Client.RunJobAsync(Request());

                break;
            }
            catch (RpcException exception) when (exception.StatusCode == StatusCode.ResourceExhausted
                                              && timeout.Elapsed < TimeSpan.FromSeconds(20))
            {
                await Task.Delay(10);
            }
        }

        Assert.That(Directory.Exists(app.Runner.LastWorkspace), Is.False);
    }

    private static Wire.RunJobRequest Request()
    {
        var request = new Wire.RunJobRequest
        {
            JobId = long.MaxValue,
            Language = "cpp",
            LanguageVersion = "23",
            ResultPrefix = "run",
            Source = Reference("source"),
            CompilationLimits = Limits(),
            ExecutionLimits = Limits()
        };

        request.Tests.AddRange(
            Ids.Select(id => new Wire.JobTest
                {
                    TestId = id,
                    Input = Reference("input-" + id)
                }
            )
        );

        return request;
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CancellationStopsPreparationOrPublicationWithoutLaterWork(bool publication)
    {
        await using var app = new TestApplication();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task Block(CancellationToken token)
        {
            entered.TrySetResult();

            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            finally
            {
                stopped.TrySetResult();
            }
        }

        if (publication) app.Storage.BeforeUpload = Block;
        else app.Storage.BeforeDownload = Block;

        using var cancellation = new CancellationTokenSource();
        using var call = app.Client.RunJobAsync(Request(), cancellationToken: cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await cancellation.CancelAsync();
        Assert.ThrowsAsync<RpcException>(async () => await call.ResponseAsync);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.That(app.Runner.Calls, Is.EqualTo(publication ? 1 : 0));
        Assert.That(app.Storage.Uploads, Has.Count.EqualTo(publication ? 1 : 0));

        Assert.That(
            (await app.Health.CheckAsync(new Grpc.Health.V1.HealthCheckRequest())).Status,
            Is.EqualTo(Grpc.Health.V1.HealthCheckResponse.Types.ServingStatus.Serving)
        );
    }

    [Test]
    public async Task PreCancelledCallDoesNotExecuteAndHealthRemainsAvailable()
    {
        await using var app = new TestApplication();
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        Assert.ThrowsAsync<RpcException>(async () => await app.Client.RunJobAsync(
                                                         Request(),
                                                         cancellationToken: canceled.Token
                                                     )
        );

        Assert.That(app.Runner.Calls, Is.Zero);

        Assert.That(
            (await app.Health.CheckAsync(new Grpc.Health.V1.HealthCheckRequest())).Status,
            Is.EqualTo(Grpc.Health.V1.HealthCheckResponse.Types.ServingStatus.Serving)
        );
    }

    [Test]
    public async Task JobTraceIsIndependentAndMetricLabelsDoNotContainIds()
    {
        var activities = new System.Collections.Concurrent.ConcurrentBag<System.Diagnostics.Activity>();

        using var activityListener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source => source.Name == "Texnokaktus.ProgOlymp.RunnerAgent",
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add
        };

        System.Diagnostics.ActivitySource.AddActivityListener(activityListener);
        var labels = new System.Collections.Concurrent.ConcurrentBag<string>();
        using var meterListener = new System.Diagnostics.Metrics.MeterListener();

        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "Texnokaktus.ProgOlymp.RunnerAgent")
                listener.EnableMeasurementEvents(instrument);
        };

        meterListener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            {
                foreach (var tag in tags) labels.Add(tag.Key);
            }
        );

        meterListener.Start();
        await using var app = new TestApplication();
        using var parent = new System.Diagnostics.Activity("external-parent").Start();
        await app.Client.RunJobAsync(Request());
        var root = activities.Single(activity => activity.OperationName == "runner.job");
        Assert.That(root.ParentId, Is.Null);
        Assert.That(root.TraceId, Is.Not.EqualTo(parent.TraceId));
        Assert.That(root.GetTagItem("job.id"), Is.EqualTo(long.MaxValue));
        Assert.That(labels, Does.Not.Contain("job.id").And.Not.Contain("test.id"));
        Assert.That(labels, Does.Contain("outcome"));
    }

    [Test]
    public async Task TransportDoesNotImposeDefaultFourMegabyteRequestLimit()
    {
        await using var app = new TestApplication();
        var request = Request();
        request.Source.Key = new string('a', 5 * 1024 * 1024);
        request.Source.Sha256 = ByteString.Empty;
        var exception = Assert.ThrowsAsync<RpcException>(async () => await app.Client.RunJobAsync(request));
        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
        Assert.That(app.Storage.Downloads, Is.Zero);
    }

    private static Wire.ObjectReference Reference(string key) => new()
    {
        Key = key,
        Sha256 = ByteString.CopyFrom(new byte[32])
    };

    private static Wire.ResourceLimits Limits() => new()
    {
        CpuTimeMs = 1000,
        WallTimeMs = 2000,
        MemoryBytes = 64000000,
        StdoutBytes = 1000,
        StderrBytes = 1000,
        Processes = 8
    };

    private sealed class TestApplication : IAsyncDisposable
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly GrpcChannel _channel;
        public FakeStorage Storage { get; } = new();
        public FakeRunner Runner { get; } = new();
        public Wire.Runner.RunnerClient Client { get; }
        public Grpc.Health.V1.Health.HealthClient Health { get; }

        public TestApplication()
        {
            _factory = new CustomWebApplicationFactory().WithWebHostBuilder(builder =>
                                                                                builder
                                                                                   .ConfigureTestServices(services =>
                                                                                        {
                                                                                            services.Replace(
                                                                                                ServiceDescriptor
                                                                                                   .Singleton<
                                                                                                        IObjectStorage>(
                                                                                                        Storage
                                                                                                    )
                                                                                            );

                                                                                            services.Replace(
                                                                                                ServiceDescriptor
                                                                                                   .Singleton<
                                                                                                        ISubmissionRunner>(
                                                                                                        Runner
                                                                                                    )
                                                                                            );
                                                                                        }
                                                                                    )
            );

            _channel = GrpcChannel.ForAddress(
                "http://localhost",
                new GrpcChannelOptions
                {
                    HttpClient = _factory.CreateDefaultClient(),
                    DisposeHttpClient = true,
                    MaxReceiveMessageSize = null
                }
            );

            Client = new(_channel);
            Health = new(_channel);
        }

        public async ValueTask DisposeAsync()
        {
            _channel.Dispose();
            await _factory.DisposeAsync();
        }
    }

    private sealed class FakeStorage : IObjectStorage
    {
        public int Downloads { get; private set; }
        public List<string> Uploads { get; } = [];
        public string? FailedUpload { get; set; }
        public bool FailSource { get; set; }
        public Func<CancellationToken, Task>? BeforeDownload { get; set; }
        public Func<CancellationToken, Task>? BeforeUpload { get; set; }

        public async Task DownloadAsync(
            ObjectRole role,
            string key,
            ReadOnlyMemory<byte> sha256,
            string path,
            CancellationToken token
        )
        {
            Downloads++;
            if (BeforeDownload is not null) await BeforeDownload(token);

            if (role == ObjectRole.Source && FailSource) throw new ObjectStorageException("hash_mismatch");

            await File.WriteAllTextAsync(path, "input", token);
        }

        public async Task<ArtifactReference> UploadAsync(string path, string key, CancellationToken token)
        {
            Uploads.Add(key);
            if (BeforeUpload is not null) await BeforeUpload(token);

            if (FailedUpload is not null && key.EndsWith(FailedUpload, StringComparison.Ordinal))
                throw new ObjectStorageException("storage_failed");

            return new(key, SHA256.HashData(await File.ReadAllBytesAsync(path, token)));
        }
    }

    private sealed class FakeRunner : ISubmissionRunner
    {
        public int Calls { get; private set; }
        public string? LastWorkspace { get; private set; }
        public Func<CancellationToken, Task>? BeforeRun { get; set; }

        public async Task<LocalSubmissionResult> RunAsync(LocalSubmission submission, CancellationToken token)
        {
            Calls++;
            LastWorkspace = submission.WorkspacePath;
            if (BeforeRun is not null) await BeforeRun(token);

            async Task<LocalStageResult> Stage(string name, int exit)
            {
                var stdout = Path.Combine(submission.WorkspacePath, name + "-out");
                var stderr = Path.Combine(submission.WorkspacePath, name + "-err");
                await File.WriteAllTextAsync(stdout, "out", token);
                await File.WriteAllTextAsync(stderr, "err", token);

                return new(
                    exit == 0 ? TerminationReason.Success : TerminationReason.RuntimeError,
                    1,
                    2,
                    4096,
                    8192,
                    exit,
                    null,
                    stdout,
                    stderr,
                    null,
                    [],
                    []
                );
            }

            var compilation = await Stage("compilation", 0);
            var tests = new List<LocalTestResult>();

            foreach (var test in submission.Tests)
                tests.Add(
                    new(
                        test.TestId,
                        false,
                        await Stage(
                            test.TestId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            test.TestId == 2 ? 3 : 0
                        ),
                        []
                    )
                );

            return new(submission.JobId, SubmissionStatus.Completed, compilation, tests, []);
        }
    }
}

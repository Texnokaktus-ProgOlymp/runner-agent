using Texnokaktus.ProgOlymp.Platform;
using Texnokaktus.ProgOlymp.RunnerAgent.Execution;
using Texnokaktus.ProgOlymp.RunnerAgent.Languages;
using Texnokaktus.ProgOlymp.RunnerAgent.Workspace;
using Amazon.S3;
using Texnokaktus.ProgOlymp.RunnerAgent.Storage;
using Texnokaktus.ProgOlymp.RunnerAgent.Api;
using Texnokaktus.ProgOlymp.RunnerAgent.Jobs;
using Texnokaktus.ProgOlymp.RunnerAgent.Diagnostics;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

try
{
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SERVICE_NAME")))
        throw new InvalidOperationException("SERVICE_NAME must be set and non-empty.");

    var builder = WebApplication.CreateBuilder(
        new WebApplicationOptions
        {
            Args = [.. args, "--hostBuilder:reloadConfigOnChange=false"],
            ContentRootPath = AppContext.BaseDirectory
        }
    );

    var runner = new RunnerOptions();
    builder.Configuration.GetSection("Runner").Bind(runner);
    runner.Validate();
    builder.Services.AddSingleton(runner);
    builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = null);

    builder.Services.AddGrpc(options =>
        {
            options.MaxReceiveMessageSize = null;
            options.MaxSendMessageSize = null;
        }
    );

    builder.Services.AddSingleton<RunnerState>();
    builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = Timeout.InfiniteTimeSpan);
    builder.Services.AddSingleton<RequestValidator>();
    builder.Services.AddSingleton<JobProcessor>();
    var storage = new S3Options();
    builder.Configuration.GetSection("S3").Bind(storage);
    storage.Validate();
    var credentials = S3Options.ReadCredentials();
    builder.Services.AddSingleton(storage);
    builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(credentials, storage.CreateClientConfig()));
    builder.Services.AddSingleton<IObjectStorage, S3ObjectStorage>();
    builder.Services.AddSingleton<ArtifactPublisher>();
    var profile = new LanguageProfile();
    builder.Configuration.GetSection("LanguageProfile").Bind(profile);
    profile.Validate();
    builder.Services.AddSingleton<ILanguageProvider>(new ConfiguredLanguageProvider(profile));
    builder.Services.AddSingleton<IsolateSandbox>();
    builder.Services.AddSingleton<ISubmissionRunner, IsolateSubmissionRunner>();
    builder.Services.AddSingleton<ISandboxEnvironment, SandboxEnvironment>();
    builder.Services.AddHostedService<SandboxStartupService>();
    builder.Services.AddSingleton(new WorkspaceManager(runner.WorkspaceRoot));
    builder.UsePlatform();

    builder.Services.AddOpenTelemetry()
           .WithTracing(tracing => tracing.AddSource(RunnerTelemetry.Name))
           .WithMetrics(metrics => metrics.AddMeter(RunnerTelemetry.Name));

    builder.Services.AddSingleton<IHostLifetime, RunnerLifetime>();

    await using var app = builder.Build();
    app.MapGrpcService<RunnerService>();
    app.MapGrpcService<RunnerHealthService>();
    var state = app.Services.GetRequiredService<RunnerState>();
    await app.RunAsync();

    return state.ExitCode;
}
catch (Exception)
{
    Console.Error.WriteLine(
        "RunnerAgent startup or host failure. Check SERVICE_NAME, AWS credentials, S3, LanguageProfile and sandbox configuration."
    );

    return 1;
}

public partial class Program
{
}

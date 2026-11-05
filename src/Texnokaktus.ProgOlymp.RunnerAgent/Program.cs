using Texnokaktus.ProgOlymp.Platform;
using Texnokaktus.ProgOlymp.RunnerAgent.Execution;
using Texnokaktus.ProgOlymp.RunnerAgent.Languages;
using Texnokaktus.ProgOlymp.RunnerAgent.Workspace;

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
builder.Services.AddSingleton<ILanguageProvider, CppLanguageProvider>();
builder.Services.AddSingleton<IsolateSandbox>();
builder.Services.AddSingleton<ISubmissionRunner, IsolateSubmissionRunner>();
builder.Services.AddSingleton(new WorkspaceManager(runner.WorkspaceRoot));
builder.UsePlatform();

await using var app = builder.Build();
await app.RunAsync();

public partial class Program
{
}

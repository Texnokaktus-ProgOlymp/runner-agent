namespace Texnokaktus.ProgOlymp.RunnerAgent.Execution;

internal sealed class SandboxStartupService(ISandboxEnvironment environment) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => environment.InitializeAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

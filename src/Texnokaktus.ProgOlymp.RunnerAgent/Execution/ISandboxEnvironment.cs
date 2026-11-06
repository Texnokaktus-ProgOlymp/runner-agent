namespace Texnokaktus.ProgOlymp.RunnerAgent.Execution;

public interface ISandboxEnvironment
{
    Task InitializeAsync(CancellationToken cancellationToken);
}

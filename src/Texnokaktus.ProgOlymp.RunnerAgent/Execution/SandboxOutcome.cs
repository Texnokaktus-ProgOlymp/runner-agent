namespace Texnokaktus.ProgOlymp.RunnerAgent.Execution;

internal sealed record SandboxOutcome(
    LocalStageResult? Execution,
    IReadOnlyList<InfrastructureDiagnostic> Diagnostics,
    bool EnvironmentReusable
);

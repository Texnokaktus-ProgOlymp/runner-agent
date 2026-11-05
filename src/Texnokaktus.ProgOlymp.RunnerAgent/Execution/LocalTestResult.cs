namespace Texnokaktus.ProgOlymp.RunnerAgent.Execution;

public sealed record LocalTestResult(
    int TestId,
    bool InfrastructureError,
    LocalStageResult? Execution,
    IReadOnlyList<InfrastructureDiagnostic> Diagnostics
);

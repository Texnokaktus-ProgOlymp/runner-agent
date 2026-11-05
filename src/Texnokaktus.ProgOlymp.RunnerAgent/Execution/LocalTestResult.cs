namespace Texnokaktus.ProgOlymp.RunnerAgent.Execution;

internal sealed record LocalTestResult(
    int TestId,
    bool InfrastructureError,
    LocalStageResult? Execution,
    IReadOnlyList<InfrastructureDiagnostic> Diagnostics
);

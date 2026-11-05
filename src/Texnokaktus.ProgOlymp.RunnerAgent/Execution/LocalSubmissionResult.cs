namespace Texnokaktus.ProgOlymp.RunnerAgent.Execution;

internal sealed record LocalSubmissionResult(
    long JobId,
    SubmissionStatus Status,
    LocalStageResult? Compilation,
    IReadOnlyList<LocalTestResult> Tests,
    IReadOnlyList<InfrastructureDiagnostic> Diagnostics
);

namespace Texnokaktus.ProgOlymp.RunnerAgent.Execution;

public sealed record LocalStageResult(
    TerminationReason? Reason,
    long? CpuTimeMs,
    long? WallTimeMs,
    long? PeakMemoryBytes,
    long? AppliedMemoryLimitBytes,
    int? ExitCode,
    int? Signal,
    string? StdoutPath,
    string? StderrPath,
    string? MetadataPath,
    IReadOnlyList<InfrastructureDiagnostic> Diagnostics,
    IReadOnlyList<TerminationReason> Violations
);

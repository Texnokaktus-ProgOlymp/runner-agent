namespace Texnokaktus.ProgOlymp.RunnerAgent.Execution;

internal sealed record LocalSubmission(
    long JobId,
    string SourcePath,
    IReadOnlyList<LocalTest> Tests,
    ResourceLimits CompilationLimits,
    ResourceLimits ExecutionLimits
);

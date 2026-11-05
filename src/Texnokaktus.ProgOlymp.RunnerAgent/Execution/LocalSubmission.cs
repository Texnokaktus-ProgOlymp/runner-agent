namespace Texnokaktus.ProgOlymp.RunnerAgent.Execution;

public sealed record LocalSubmission(
    long JobId,
    string WorkspacePath,
    string SourcePath,
    IReadOnlyList<LocalTest> Tests,
    ResourceLimits CompilationLimits,
    ResourceLimits ExecutionLimits
);

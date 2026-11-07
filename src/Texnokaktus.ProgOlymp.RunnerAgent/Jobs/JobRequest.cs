using Texnokaktus.ProgOlymp.RunnerAgent.Execution;

namespace Texnokaktus.ProgOlymp.RunnerAgent.Jobs;

internal sealed record InputReference(string Key, byte[] Sha256);

internal sealed record JobTest(int TestId, InputReference Input);

internal sealed record JobRequest(
    long JobId,
    InputReference Source,
    IReadOnlyList<JobTest> Tests,
    string ResultPrefix,
    ResourceLimits CompilationLimits,
    ResourceLimits ExecutionLimits
);

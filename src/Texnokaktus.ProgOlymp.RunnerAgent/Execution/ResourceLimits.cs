namespace Texnokaktus.ProgOlymp.RunnerAgent.Execution;

internal sealed record ResourceLimits(
    long CpuTimeMs,
    long WallTimeMs,
    long MemoryBytes,
    long StdoutBytes,
    long StderrBytes,
    int Processes
)
{
    public void Validate()
    {
        if (CpuTimeMs <= 0
         || WallTimeMs <= 0
         || MemoryBytes <= 0
         || StdoutBytes <= 0
         || StderrBytes <= 0
         || Processes <= 0)
            throw new ArgumentException("Every resource limit must be positive.");
    }
}

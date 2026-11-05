namespace Texnokaktus.ProgOlymp.RunnerAgent.Execution;

public sealed record ResourceLimits(
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

        if (CpuTimeMs > int.MaxValue || WallTimeMs > int.MaxValue)
            throw new ArgumentException("The isolate timer accepts signed 32-bit milliseconds.");

        _ = AppliedMemoryBytes;
    }

    public long AppliedMemoryBytes
    {
        get
        {
            var page = Environment.SystemPageSize;
            var rounded = checked((MemoryBytes + page - 1) / page * page);

            if (page % 1024 != 0 || rounded / 1024 > int.MaxValue)
                throw new ArgumentException("The memory limit cannot be applied by isolate.");

            return rounded;
        }
    }
}

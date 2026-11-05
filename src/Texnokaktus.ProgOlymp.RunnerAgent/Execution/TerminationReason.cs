namespace Texnokaktus.ProgOlymp.RunnerAgent.Execution;

public enum TerminationReason
{
    Success,
    RuntimeError,
    CpuTimeLimitExceeded,
    WallTimeLimitExceeded,
    MemoryLimitExceeded,
    OutputLimitExceeded,
    ProcessLimitExceeded
}

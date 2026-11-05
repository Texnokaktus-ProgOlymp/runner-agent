namespace Texnokaktus.ProgOlymp.RunnerAgent.Execution;

internal enum TerminationReason
{
    Success,
    RuntimeError,
    CpuTimeLimitExceeded,
    WallTimeLimitExceeded,
    MemoryLimitExceeded,
    OutputLimitExceeded,
    ProcessLimitExceeded
}

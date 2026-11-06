namespace Texnokaktus.ProgOlymp.RunnerAgent.Execution;

internal static class TerminationClassifier
{
    public static IReadOnlyList<TerminationReason> Classify(
        ResourceLimits limits,
        long? cpuTimeMs,
        long? wallTimeMs,
        long? oomKills,
        bool outputExceeded,
        long? blockedProcesses,
        int? exitCode,
        int? signal
    )
    {
        var reasons = new List<TerminationReason>();
        if (oomKills > 0) reasons.Add(TerminationReason.MemoryLimitExceeded);
        if (outputExceeded) reasons.Add(TerminationReason.OutputLimitExceeded);
        if (cpuTimeMs > limits.CpuTimeMs) reasons.Add(TerminationReason.CpuTimeLimitExceeded);
        if (wallTimeMs > limits.WallTimeMs) reasons.Add(TerminationReason.WallTimeLimitExceeded);
        if (blockedProcesses > 0) reasons.Add(TerminationReason.ProcessLimitExceeded);
        if (signal is not null || exitCode is not (null or 0)) reasons.Add(TerminationReason.RuntimeError);

        return reasons;
    }
}

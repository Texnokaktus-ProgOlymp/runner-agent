using Texnokaktus.ProgOlymp.RunnerAgent.Execution;

namespace Texnokaktus.ProgOlymp.RunnerAgent.Jobs;

internal static partial class RunnerLog
{
    [LoggerMessage(5, LogLevel.Information, "Application started.")]
    public static partial void HostStarted(ILogger logger);

    [LoggerMessage(1, LogLevel.Information, "Job {JobId} started.")]
    public static partial void Started(ILogger logger, long jobId);

    [LoggerMessage(2, LogLevel.Information, "Job {JobId} finished with {Status}.")]
    public static partial void Finished(ILogger logger, long jobId, SubmissionStatus status);

    [LoggerMessage(3, LogLevel.Information, "Job {JobId} canceled after cleanup.")]
    public static partial void Canceled(ILogger logger, long jobId);

    [LoggerMessage(4, LogLevel.Error, "Job {JobId} workspace cleanup failed.")]
    public static partial void CleanupFailed(ILogger logger, long jobId);

    [LoggerMessage(
        6,
        LogLevel.Information,
        "Job {JobId} test {TestId} finished: {Reason}; infrastructure error {InfrastructureError}."
    )]
    public static partial void TestFinished(
        ILogger logger,
        long jobId,
        int testId,
        TerminationReason? reason,
        bool infrastructureError
    );

    [LoggerMessage(7, LogLevel.Error, "Job {JobId} sandbox cleanup failed; the worker will stop.")]
    public static partial void SandboxCleanupFailed(ILogger logger, long jobId);
}

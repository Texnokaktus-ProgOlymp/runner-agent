namespace Texnokaktus.ProgOlymp.RunnerAgent.Execution;

internal interface ISubmissionRunner
{
    Task<LocalSubmissionResult> RunAsync(LocalSubmission submission, CancellationToken cancellationToken);
}

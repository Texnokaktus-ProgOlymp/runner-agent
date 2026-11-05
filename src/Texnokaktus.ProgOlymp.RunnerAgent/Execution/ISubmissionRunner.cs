namespace Texnokaktus.ProgOlymp.RunnerAgent.Execution;

public interface ISubmissionRunner
{
    Task<LocalSubmissionResult> RunAsync(LocalSubmission submission, CancellationToken cancellationToken);
}

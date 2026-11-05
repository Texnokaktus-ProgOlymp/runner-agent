using Texnokaktus.ProgOlymp.RunnerAgent.Languages;

namespace Texnokaktus.ProgOlymp.RunnerAgent.Execution;

internal sealed class IsolateSubmissionRunner(ILanguageProvider language, IsolateSandbox sandbox) : ISubmissionRunner
{
    public async Task<LocalSubmissionResult> RunAsync(LocalSubmission submission, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("isolate requires Linux.");

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(submission.JobId);
        submission.CompilationLimits.Validate();
        submission.ExecutionLimits.Validate();

        if (submission.Tests.Count == 0
         || submission.Tests.Any(test => test.TestId <= 0)
         || submission.Tests.Select(test => test.TestId).Distinct().Count() != submission.Tests.Count)
            throw new ArgumentException("Tests must have unique positive IDs and must not be empty.");

        cancellationToken.ThrowIfCancellationRequested();

        var tests = new List<LocalTestResult>();
        var executable = Path.Combine(submission.WorkspacePath, "solution");

        var compilation = await sandbox.RunAsync(
                              "compilation",
                              language.CompilationCommand,
                              submission.SourcePath,
                              "source.cpp",
                              null,
                              Path.Combine(submission.WorkspacePath, "compilation"),
                              submission.CompilationLimits,
                              executable,
                              cancellationToken
                          );

        var diagnostics = compilation.Diagnostics.ToList();

        if (!compilation.EnvironmentReusable || diagnostics.Count != 0)
            return new(
                submission.JobId,
                SubmissionStatus.InfrastructureError,
                compilation.Execution,
                tests,
                diagnostics,
                compilation.EnvironmentReusable
            );

        if (compilation.Execution?.Reason != TerminationReason.Success)
            return new(submission.JobId, SubmissionStatus.CompilationError, compilation.Execution, tests, diagnostics);

        foreach (var test in submission.Tests)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outcome = await sandbox.RunAsync(
                              "execution",
                              language.ExecutionCommand,
                              executable,
                              "solution",
                              test.InputPath,
                              Path.Combine(
                                  submission.WorkspacePath,
                                  "tests",
                                  test.TestId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                              ),
                              submission.ExecutionLimits,
                              null,
                              cancellationToken
                          );

            tests.Add(new(test.TestId, outcome.Diagnostics.Count != 0, outcome.Execution, outcome.Diagnostics));

            if (!outcome.EnvironmentReusable)
            {
                diagnostics.AddRange(outcome.Diagnostics);

                return new(
                    submission.JobId,
                    SubmissionStatus.InfrastructureError,
                    compilation.Execution,
                    tests,
                    diagnostics,
                    false
                );
            }
        }

        return new(submission.JobId, SubmissionStatus.Completed, compilation.Execution, tests, diagnostics);
    }
}

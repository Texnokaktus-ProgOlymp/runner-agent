using System.Globalization;
using Texnokaktus.ProgOlymp.RunnerAgent.Diagnostics;
using Texnokaktus.ProgOlymp.RunnerAgent.Execution;
using Texnokaktus.ProgOlymp.RunnerAgent.Storage;
using Texnokaktus.ProgOlymp.RunnerAgent.Workspace;

namespace Texnokaktus.ProgOlymp.RunnerAgent.Jobs;

internal sealed record JobExecutionResult(
    LocalSubmissionResult Result,
    SubmissionPublication Publication,
    List<InfrastructureDiagnostic> Diagnostics
);

internal sealed class JobProcessor(
    WorkspaceManager workspaces,
    IObjectStorage storage,
    ISubmissionRunner runner,
    ArtifactPublisher publisher,
    RunnerState state,
    ILogger<JobProcessor> logger
)
{
    public async Task<JobExecutionResult> RunAsync(JobRequest request, CancellationToken cancellationToken)
    {
        var diagnostics = new List<InfrastructureDiagnostic>();
        var result = new LocalSubmissionResult(request.JobId, SubmissionStatus.InfrastructureError, null, [], []);
        var publication = new SubmissionPublication(null, new Dictionary<int, StagePublication>());
        JobWorkspace workspace;

        try
        {
            workspace = workspaces.Create(request.JobId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new("workspace", "workspace_create_failed"));
            state.BeginStop(fatal: true);

            return new(result, publication, diagnostics);
        }

        try
        {
            var source = Path.Combine(workspace.Path, "source");
            var sourceReady = true;

            try
            {
                await storage.DownloadAsync(
                    ObjectRole.Source,
                    request.Source.Key,
                    request.Source.Sha256,
                    source,
                    cancellationToken
                );
            }
            catch (ObjectStorageException exception)
            {
                sourceReady = false;
                diagnostics.Add(new("source", exception.Code));
            }

            if (sourceReady)
            {
                var tests = new List<LocalTest>();

                foreach (var test in request.Tests)
                {
                    using var inputActivity = RunnerTelemetry.Activities.StartActivity("runner.input");
                    inputActivity?.SetTag("test.id", test.TestId);
                    cancellationToken.ThrowIfCancellationRequested();

                    var input = Path.Combine(
                        workspace.Path,
                        "input-" + test.TestId.ToString(CultureInfo.InvariantCulture)
                    );

                    IReadOnlyList<InfrastructureDiagnostic>? errors = null;

                    try
                    {
                        await storage.DownloadAsync(
                            ObjectRole.Data,
                            test.Input.Key,
                            test.Input.Sha256,
                            input,
                            cancellationToken
                        );
                    }
                    catch (ObjectStorageException exception)
                    {
                        errors = [new("input", exception.Code)];
                    }

                    tests.Add(new(test.TestId, input, errors));
                }

                result = await runner.RunAsync(
                             new(
                                 request.JobId,
                                 workspace.Path,
                                 source,
                                 tests,
                                 request.CompilationLimits,
                                 request.ExecutionLimits
                             ),
                             cancellationToken
                         );

                if (!result.EnvironmentReusable) state.BeginStop(fatal: true);

                foreach (var test in result.Tests)
                    RunnerLog.TestFinished(
                        logger,
                        request.JobId,
                        test.TestId,
                        test.Execution?.Reason,
                        test.InfrastructureError
                    );

                publication = await publisher.PublishAsync(result, request.ResultPrefix, cancellationToken);
            }
        }
        catch (SandboxCleanupException)
        {
            RunnerLog.SandboxCleanupFailed(logger, request.JobId);
            state.BeginStop(fatal: true);

            throw;
        }
        finally
        {
            using var cleanupActivity = RunnerTelemetry.Activities.StartActivity("runner.cleanup");

            try
            {
                workspace.Dispose();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(new("cleanup", "workspace_cleanup_failed"));
                RunnerLog.CleanupFailed(logger, request.JobId);
                state.BeginStop(fatal: true);
            }
        }

        return new(result, publication, diagnostics);
    }
}

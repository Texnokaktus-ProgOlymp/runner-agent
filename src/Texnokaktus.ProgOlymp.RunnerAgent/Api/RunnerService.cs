using Grpc.Core;
using System.Diagnostics;
using Texnokaktus.ProgOlymp.RunnerAgent.Diagnostics;
using Texnokaktus.ProgOlymp.RunnerAgent.Execution;
using Texnokaktus.ProgOlymp.RunnerAgent.Jobs;
using Wire = Texnokaktus.ProgOlymp.RunnerAgent.Contracts;

namespace Texnokaktus.ProgOlymp.RunnerAgent.Api;

internal sealed class RunnerService(
    RunnerState state,
    RequestValidator validator,
    JobProcessor processor,
    ILogger<RunnerService> logger
) : Wire.Runner.RunnerBase
{
    public override async Task<Wire.RunJobResponse> RunJob(Wire.RunJobRequest request, ServerCallContext context)
    {
        var admission = state.TryEnter();

        if (admission != Admission.Accepted)
        {
            RunnerTelemetry.Rejections.Add(1, new KeyValuePair<string, object?>("reason", admission.ToString()));

            throw new RpcException(
                new Status(
                    admission == Admission.Busy ? StatusCode.ResourceExhausted : StatusCode.Unavailable,
                    admission == Admission.Busy ? "Runner is busy." : "Runner is stopping."
                )
            );
        }

        var parent = Activity.Current;
        Activity? activity = null;
        var outcome = "invalid";
        var valid = false;

        try
        {
            var job = validator.Validate(request);
            valid = true;
            Activity.Current = null;

            activity = RunnerTelemetry.Activities.StartActivity(
                "runner.job",
                ActivityKind.Internal,
                default(ActivityContext),
                links: parent is null ? null : [new ActivityLink(parent.Context)]
            );

            activity?.SetTag("job.id", job.JobId);
            RunnerLog.Started(logger, job.JobId);
            var result = await processor.RunAsync(job, context.CancellationToken);
            outcome = result.Result.Status.ToString();
            RunnerLog.Finished(logger, job.JobId, result.Result.Status);

            return ResultMapper.Map(result);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            outcome = "canceled";
            RunnerLog.Canceled(logger, request.JobId);

            throw new RpcException(
                new Status(
                    context.Status.StatusCode == StatusCode.DeadlineExceeded || context.Deadline <= DateTime.UtcNow
                        ? StatusCode.DeadlineExceeded
                        : StatusCode.Cancelled,
                    "RunJob canceled."
                )
            );
        }
        catch (SandboxCleanupException)
        {
            outcome = "infrastructure_error";
            state.BeginStop(fatal: true);

            throw new RpcException(new Status(StatusCode.Internal, "Sandbox cleanup failed."));
        }
        finally
        {
            activity?.SetTag("job.outcome", outcome);
            activity?.Dispose();
            Activity.Current = parent;

            if (valid) RunnerTelemetry.Jobs.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
            else RunnerTelemetry.Rejections.Add(1, new KeyValuePair<string, object?>("reason", "invalid"));

            state.Leave();
        }
    }
}

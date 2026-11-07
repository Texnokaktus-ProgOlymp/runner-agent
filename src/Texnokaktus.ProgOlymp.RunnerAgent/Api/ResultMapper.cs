using Google.Protobuf;
using Texnokaktus.ProgOlymp.RunnerAgent.Execution;
using Texnokaktus.ProgOlymp.RunnerAgent.Jobs;
using Texnokaktus.ProgOlymp.RunnerAgent.Storage;
using Wire = Texnokaktus.ProgOlymp.RunnerAgent.Contracts;

namespace Texnokaktus.ProgOlymp.RunnerAgent.Api;

internal static class ResultMapper
{
    public static Wire.RunJobResponse Map(JobExecutionResult execution)
    {
        var local = execution.Result;

        var response = new Wire.RunJobResponse
        {
            JobId = local.JobId,
            Status = (Wire.JobStatus)((int)local.Status + 1)
        };

        response.Diagnostics.AddRange(local.Diagnostics.Concat(execution.Diagnostics).Select(Diagnostic));

        if (local.Compilation is not null)
        {
            response.Compilation = Stage(local.Compilation, execution.Publication.Compilation);

            if (execution.Publication.Compilation is { } compilation)
                response.Diagnostics.AddRange(compilation.Diagnostics.Select(Diagnostic));
        }

        foreach (var test in local.Tests)
        {
            execution.Publication.Tests.TryGetValue(test.TestId, out var publication);

            var item = new Wire.TestResult
            {
                TestId = test.TestId,
                Status = test.InfrastructureError || publication?.Diagnostics.Count > 0
                             ? Wire.TestStatus.InfrastructureError
                             : Wire.TestStatus.Completed
            };

            item.Diagnostics.AddRange(test.Diagnostics.Select(Diagnostic));
            if (publication is not null) item.Diagnostics.AddRange(publication.Diagnostics.Select(Diagnostic));
            if (test.Execution is not null) item.Execution = Stage(test.Execution, publication);
            response.Tests.Add(item);
        }

        if (local.Status == SubmissionStatus.CompilationError) response.FailureStage = "compilation";
        else if (local.Status == SubmissionStatus.InfrastructureError)
            response.FailureStage = response.Diagnostics.FirstOrDefault()?.Stage ?? "execution";

        return response;
    }

    private static Wire.StageResult Stage(LocalStageResult stage, StagePublication? publication)
    {
        var result = new Wire.StageResult();
        if (stage.Reason is { } reason) result.Reason = (Wire.TerminationReason)((int)reason + 1);
        if (stage.CpuTimeMs is { } cpu) result.CpuTimeMs = cpu;
        if (stage.WallTimeMs is { } wall) result.WallTimeMs = wall;
        if (stage.PeakMemoryBytes is { } peak) result.PeakMemoryBytes = peak;
        if (stage.AppliedMemoryLimitBytes is { } applied) result.AppliedMemoryLimitBytes = applied;
        if (stage.ExitCode is { } exit) result.ExitCode = exit;
        if (stage.Signal is { } signal) result.Signal = signal;
        result.Diagnostics.AddRange(stage.Diagnostics.Select(Diagnostic));
        result.Violations.AddRange(stage.Violations.Select(reason => (Wire.TerminationReason)((int)reason + 1)));

        if (publication is not null)
        {
            if (publication.Stdout is not null) result.Stdout = Reference(publication.Stdout);
            if (publication.Stderr is not null) result.Stderr = Reference(publication.Stderr);
            result.Diagnostics.AddRange(publication.Diagnostics.Select(Diagnostic));
        }

        return result;
    }

    private static Wire.ObjectReference Reference(ArtifactReference artifact) => new()
    {
        Key = artifact.Key,
        Sha256 = ByteString.CopyFrom(artifact.Sha256)
    };

    private static Wire.Diagnostic Diagnostic(InfrastructureDiagnostic diagnostic) => new()
    {
        Stage = diagnostic.Stage,
        Code = diagnostic.Code
    };
}

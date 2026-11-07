using Grpc.Core;
using Texnokaktus.ProgOlymp.RunnerAgent.Jobs;
using Texnokaktus.ProgOlymp.RunnerAgent.Languages;
using Wire = Texnokaktus.ProgOlymp.RunnerAgent.Contracts;
using Limits = Texnokaktus.ProgOlymp.RunnerAgent.Execution.ResourceLimits;

namespace Texnokaktus.ProgOlymp.RunnerAgent.Api;

internal sealed class RequestValidator(ILanguageProvider language)
{
    public JobRequest Validate(Wire.RunJobRequest request)
    {
        try
        {
            if (request.JobId <= 0
             || !language.Supports(request.Language, request.LanguageVersion)
             || request.Tests.Count == 0
             || request.ResultPrefix.Length == 0)
                throw new ArgumentException("Invalid job request.");

            var compilation = LimitsFrom(request.CompilationLimits);
            var execution = LimitsFrom(request.ExecutionLimits);
            var source = ReferenceFrom(request.Source);
            var ids = new HashSet<int>();
            var tests = new List<JobTest>();

            foreach (var test in request.Tests)
            {
                if (test.TestId <= 0 || !ids.Add(test.TestId)) throw new ArgumentException("Invalid job request.");

                tests.Add(new(test.TestId, ReferenceFrom(test.Input)));
            }

            return new(request.JobId, source, tests, request.ResultPrefix, compilation, execution);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            throw new RpcException(
                new Status(
                    StatusCode.InvalidArgument,
                    "Invalid job identifiers, profile, references or resource limits."
                )
            );
        }
    }

    private static InputReference ReferenceFrom(Wire.ObjectReference? reference)
    {
        if (reference is null || reference.Key.Length == 0 || reference.Sha256.Length != 32)
            throw new ArgumentException("Invalid job request.");

        return new(reference.Key, reference.Sha256.ToByteArray());
    }

    private static Limits LimitsFrom(Wire.ResourceLimits? limits)
    {
        if (limits is null) throw new ArgumentException("Invalid job request.");

        var result = new Limits(
            limits.CpuTimeMs,
            limits.WallTimeMs,
            limits.MemoryBytes,
            limits.StdoutBytes,
            limits.StderrBytes,
            limits.Processes
        );

        result.Validate();

        return result;
    }
}

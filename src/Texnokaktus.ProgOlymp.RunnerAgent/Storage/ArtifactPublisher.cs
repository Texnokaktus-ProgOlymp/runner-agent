using Texnokaktus.ProgOlymp.RunnerAgent.Execution;

namespace Texnokaktus.ProgOlymp.RunnerAgent.Storage;

internal sealed record StagePublication(
    ArtifactReference? Stdout,
    ArtifactReference? Stderr,
    IReadOnlyList<InfrastructureDiagnostic> Diagnostics
);

internal sealed record SubmissionPublication(
    StagePublication? Compilation,
    IReadOnlyDictionary<int, StagePublication> Tests
);

internal sealed class ArtifactPublisher(IObjectStorage storage)
{
    public async Task<SubmissionPublication> PublishAsync(
        LocalSubmissionResult result,
        string prefix,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);

        var compilation = result.Compilation is null
                              ? null
                              : await PublishStageAsync(
                                    result.Compilation,
                                    "compilation",
                                    "compilation",
                                    prefix,
                                    cancellationToken
                                );

        var tests = new Dictionary<int, StagePublication>();

        foreach (var test in result.Tests)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (test.Execution is not null)
                tests.Add(
                    test.TestId,
                    await PublishStageAsync(
                        test.Execution,
                        "execution",
                        "tests/" + test.TestId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        prefix,
                        cancellationToken
                    )
                );
        }

        return new(compilation, tests);
    }

    private async Task<StagePublication> PublishStageAsync(
        LocalStageResult result,
        string stage,
        string suffix,
        string prefix,
        CancellationToken cancellationToken
    )
    {
        var diagnostics = new List<InfrastructureDiagnostic>();

        async Task<ArtifactReference?> PublishStreamAsync(string? path, string stream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (path is null) return null;

            try
            {
                return await storage.UploadAsync(
                           path,
                           ObjectKeys.Artifact(prefix, suffix + "/" + stream + ".txt"),
                           cancellationToken
                       );
            }
            catch (Exception exception) when (exception is ObjectStorageException
                                                        or IOException
                                                        or UnauthorizedAccessException)
            {
                diagnostics.Add(new(stage, stream + "_upload_failed"));

                return null;
            }
        }

        var stdout = await PublishStreamAsync(result.StdoutPath, "stdout");
        var stderr = await PublishStreamAsync(result.StderrPath, "stderr");

        return new(stdout, stderr, diagnostics);
    }
}

using NSubstitute;
using Texnokaktus.ProgOlymp.RunnerAgent.Execution;
using Texnokaktus.ProgOlymp.RunnerAgent.Storage;

namespace Texnokaktus.ProgOlymp.RunnerAgent.Tests;

public sealed class ArtifactPublisherTests
{
    [Test]
    public async Task FailedStreamDoesNotHideOtherArtifactsOrChangeKnownVerdict()
    {
        var storage = Substitute.For<IObjectStorage>();

        storage.UploadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(call => call.ArgAt<string>(0) == "broken"
                                    ? Task.FromException<ArtifactReference>(
                                        new ObjectStorageException("storage_failed")
                                    )
                                    : Task.FromResult(new ArtifactReference(call.ArgAt<string>(1), new byte[32]))
                );

        var compilation = Stage("compiler-out", "broken", TerminationReason.Success);
        var execution = Stage("broken", "test-err", TerminationReason.RuntimeError);

        var local = new LocalSubmissionResult(
            4,
            SubmissionStatus.Completed,
            compilation,
            [new LocalTestResult(8, false, execution, [])],
            []
        );

        var result = await new ArtifactPublisher(storage).PublishAsync(local, "run/", CancellationToken.None);

        Assert.Multiple(() =>
            {
                Assert.That(result.Compilation!.Stdout!.Key, Is.EqualTo("run/compilation/stdout.txt"));
                Assert.That(result.Compilation.Stderr, Is.Null);
                Assert.That(result.Tests[8].Stdout, Is.Null);
                Assert.That(result.Tests[8].Stderr!.Key, Is.EqualTo("run/tests/8/stderr.txt"));
                Assert.That(result.Tests[8].Diagnostics, Has.Count.EqualTo(1));
                Assert.That(local.Status, Is.EqualTo(SubmissionStatus.Completed));
                Assert.That(local.Tests[0].Execution!.Reason, Is.EqualTo(TerminationReason.RuntimeError));
                Assert.That(local.Tests[0].Execution!.ExitCode, Is.EqualTo(3));
            }
        );

        await storage.Received(4).UploadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CancellationDoesNotRequirePublishingRemainingStreams()
    {
        using var cancellation = new CancellationTokenSource();
        var storage = Substitute.For<IObjectStorage>();

        storage.UploadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(call =>
                    {
                        cancellation.Cancel();

                        return Task.FromResult(new ArtifactReference(call.ArgAt<string>(1), new byte[32]));
                    }
                );

        var result = new LocalSubmissionResult(
            1,
            SubmissionStatus.CompilationError,
            Stage("out", "err", TerminationReason.RuntimeError),
            [],
            []
        );

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
                                                           await new ArtifactPublisher(storage).PublishAsync(
                                                               result,
                                                               "run",
                                                               cancellation.Token
                                                           )
        );

        await storage.Received(1).UploadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static LocalStageResult Stage(string stdout, string stderr, TerminationReason reason) =>
        new(
            reason,
            12,
            20,
            4096,
            8192,
            reason == TerminationReason.Success ? 0 : 3,
            null,
            stdout,
            stderr,
            null,
            [],
            []
        );
}

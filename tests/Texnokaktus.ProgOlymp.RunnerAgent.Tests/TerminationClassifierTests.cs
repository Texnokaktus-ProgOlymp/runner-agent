using Texnokaktus.ProgOlymp.RunnerAgent.Execution;

namespace Texnokaktus.ProgOlymp.RunnerAgent.Tests;

public sealed class TerminationClassifierTests
{
    private static readonly ResourceLimits Limits = new(10, 20, 4096, 100, 100, 2);
    private static readonly TerminationReason[] RuntimeOnly = [TerminationReason.RuntimeError];

    private static readonly TerminationReason[] Ordered =
    [
        TerminationReason.MemoryLimitExceeded,
        TerminationReason.OutputLimitExceeded, TerminationReason.CpuTimeLimitExceeded,
        TerminationReason.WallTimeLimitExceeded, TerminationReason.ProcessLimitExceeded, TerminationReason.RuntimeError
    ];

    [Test]
    public void PreservesEveryConfirmedViolationInPriorityOrder() => Assert.That(
        TerminationClassifier.Classify(Limits, 11, 21, 1, true, 1, 3, 9),
        Is.EqualTo(Ordered)
    );

    [Test]
    public void SignalAloneDoesNotProveAnyLimitViolation() => Assert.That(
        TerminationClassifier.Classify(Limits, null, null, null, false, null, null, 9),
        Is.EqualTo(RuntimeOnly)
    );

    [Test]
    public void ExactBudgetsAndSuccessfulExitAreNotViolations() => Assert.That(
        TerminationClassifier.Classify(Limits, 10, 20, 0, false, 0, 0, null),
        Is.Empty
    );

    [Test]
    public void Exit127RemainsAProgramErrorWhenExecutionWasStarted() => Assert.That(
        TerminationClassifier.Classify(Limits, 1, 1, 0, false, 0, 127, null),
        Is.EqualTo(RuntimeOnly)
    );
}

using Texnokaktus.ProgOlymp.RunnerAgent.Execution;

namespace Texnokaktus.ProgOlymp.RunnerAgent.Tests;

public sealed class ResourceLimitsTests
{
    [TestCase(0, 1, 1, 1, 1, 1)]
    [TestCase(1, 0, 1, 1, 1, 1)]
    [TestCase(1, 1, 0, 1, 1, 1)]
    [TestCase(1, 1, 1, 0, 1, 1)]
    [TestCase(1, 1, 1, 1, 0, 1)]
    [TestCase(1, 1, 1, 1, 1, 0)]
    [TestCase(-1, 1, 1, 1, 1, 1)]
    [TestCase(1, -1, 1, 1, 1, 1)]
    [TestCase(1, 1, -1, 1, 1, 1)]
    [TestCase(1, 1, 1, -1, 1, 1)]
    [TestCase(1, 1, 1, 1, -1, 1)]
    [TestCase(1, 1, 1, 1, 1, -1)]
    public void RejectsMissingOrNonPositiveLimit(
        long cpu,
        long wall,
        long memory,
        long stdout,
        long stderr,
        int processes
    )
    {
        var limits = new ResourceLimits(cpu, wall, memory, stdout, stderr, processes);
        Assert.That(limits.Validate, Throws.ArgumentException);
    }

    [Test]
    public void AcceptsIndependentPositiveBudgets()
    {
        var compilation = new ResourceLimits(10000, 20000, 500000000, 1000, 2000, 16);
        var execution = new ResourceLimits(500, 1000, 64000000, 3000, 4000, 2);
        compilation.Validate();
        execution.Validate();
        Assert.That(compilation, Is.Not.EqualTo(execution));
    }

    [Test]
    public void RoundsMemoryUpWithoutChangingRequestedBudget()
    {
        var limits = new ResourceLimits(1, 1, 1, 1, 1, 1);
        limits.Validate();
        Assert.That(limits.AppliedMemoryBytes, Is.EqualTo(Environment.SystemPageSize));
        Assert.That(limits.MemoryBytes, Is.EqualTo(1));
    }

    [TestCase(2147483648L, 1)]
    [TestCase(1, 2147483648L)]
    public void RejectsTimersThatCannotBeRepresentedByIsolate(long cpu, long wall)
    {
        var limits = new ResourceLimits(cpu, wall, 4096, 1, 1, 1);
        Assert.That(limits.Validate, Throws.ArgumentException);
    }

    [Test]
    public void RejectsMemoryRoundingOverflow()
    {
        var limits = new ResourceLimits(1, 1, long.MaxValue, 1, 1, 1);
        Assert.That(limits.Validate, Throws.TypeOf<OverflowException>());
    }

    [Test]
    public void RejectsMemoryOutsideNativeKilobyteRangeAfterRounding()
    {
        var limits = new ResourceLimits(1, 1, (long)int.MaxValue * 1024, 1, 1, 1);
        Assert.That(limits.Validate, Throws.ArgumentException);
    }
}

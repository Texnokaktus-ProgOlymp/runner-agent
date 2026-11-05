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
}

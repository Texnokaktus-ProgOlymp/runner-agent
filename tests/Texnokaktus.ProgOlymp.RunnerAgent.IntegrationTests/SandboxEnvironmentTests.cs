using Texnokaktus.ProgOlymp.RunnerAgent.IntegrationTests.Tools;

namespace Texnokaktus.ProgOlymp.RunnerAgent.IntegrationTests;

[TestFixture, Category("Sandbox"), NonParallelizable]
public sealed class SandboxEnvironmentTests : SetupBase
{
    [Test]
    public async Task ValidatesRealEnvironmentAndReleasesOwnershipOnShutdown()
    {
        Assert.That(OperatingSystem.IsLinux(), Is.True, "Use tests/prototype/run.ps1.");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var factory = new CustomWebApplicationFactory { UseRealSandbox = true };
            using var client = factory.CreateClient();
            Assert.That(Directory.Exists("/sys/fs/cgroup/runner-agent-boxes/box-0"), Is.False);
            Assert.That(Directory.Exists("/var/lib/isolate/0"), Is.False);
            await using var competing = new CustomWebApplicationFactory { UseRealSandbox = true };
            Assert.That(() => competing.CreateClient(), Throws.Exception);
        }
    }

    [Test]
    public async Task MissingCompilerRejectsHostBeforeServing()
    {
        Assert.That(OperatingSystem.IsLinux(), Is.True, "Use tests/prototype/run.ps1.");
        const string compiler = "/usr/bin/g++";
        const string saved = "/usr/bin/g++.startup-test";
        File.Move(compiler, saved);

        try
        {
            await using var factory = new CustomWebApplicationFactory { UseRealSandbox = true };
            Assert.That(() => factory.CreateClient(), Throws.Exception);
        }
        finally
        {
            File.Move(saved, compiler);
        }
    }

    [Test]
    public async Task MissingControllerRejectsHostBeforeServing()
    {
        Assert.That(OperatingSystem.IsLinux(), Is.True, "Use tests/prototype/run.ps1.");
        const string path = "/sys/fs/cgroup/runner-agent-boxes/cgroup.subtree_control";
        await File.WriteAllTextAsync(path, "-pids");

        try
        {
            await using var factory = new CustomWebApplicationFactory { UseRealSandbox = true };
            Assert.That(() => factory.CreateClient(), Throws.Exception);
        }
        finally
        {
            await File.WriteAllTextAsync(path, "+pids");
        }
    }
}

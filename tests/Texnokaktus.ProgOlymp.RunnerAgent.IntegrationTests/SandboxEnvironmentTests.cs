using Texnokaktus.ProgOlymp.RunnerAgent.IntegrationTests.Tools;
using Microsoft.Extensions.DependencyInjection;
using Texnokaktus.ProgOlymp.RunnerAgent.Workspace;

namespace Texnokaktus.ProgOlymp.RunnerAgent.IntegrationTests;

[TestFixture, Category("Sandbox"), NonParallelizable]
public sealed class SandboxEnvironmentTests : SetupBase
{
    [TestCase("direct")]
    [TestCase("parent")]
    [TestCase("final")]
    [TestCase("nested")]
    [TestCase("root")]
    [TestCase("bin")]
    [TestCase("devices")]
    public async Task RejectsWorkspaceVisibleThroughSystemMountsBeforeServing(string kind)
    {
        Assert.That(OperatingSystem.IsLinux(), Is.True, "Use tests/prototype/run.ps1.");
        var scratch = Directory.CreateTempSubdirectory("workspace-boundary-");

        try
        {
            var link = Path.Combine(scratch.FullName, "link");

            var root = kind switch
            {
                "direct" => "/usr/local", "bin" => "/bin", "devices" => "/var/lib/runner-agent-dev", _ => link
            };

            if (kind is "parent" or "nested")
            {
                Directory.CreateSymbolicLink(link, "/usr");
                root = Path.Combine(link, "local");

                if (kind == "nested")
                {
                    var nested = Path.Combine(scratch.FullName, "nested");
                    Directory.CreateSymbolicLink(nested, root);
                    root = nested;
                }
            }
            else if (kind is "final" or "root") Directory.CreateSymbolicLink(link, kind == "root" ? "/" : "/usr/local");

            await using var factory = new CustomWebApplicationFactory("--Runner:WorkspaceRoot", root);
            Assert.That(() => factory.CreateClient(), Throws.InvalidOperationException);
            Assert.That(Directory.Exists("/usr/local/runner-agent-jobs"), Is.False);
            Assert.That(Directory.GetFiles(scratch.FullName, ".runner-agent-probe-*"), Is.Empty);
        }
        finally
        {
            scratch.Delete(true);
        }
    }

    [Test]
    public async Task CanonicalizesSafeParentSymlinkWithoutRejectingSimilarPrefix()
    {
        Assert.That(OperatingSystem.IsLinux(), Is.True, "Use tests/prototype/run.ps1.");
        var scratch = Directory.CreateTempSubdirectory("workspace-safe-");
        var nearPrefix = "/usr-local-runner-test-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(nearPrefix);

        try
        {
            var target = Directory.CreateDirectory(Path.Combine(scratch.FullName, "target", "child"));
            var existing = Path.Combine(target.FullName, "unrelated");
            await File.WriteAllTextAsync(existing, "preserve");
            Directory.CreateSymbolicLink(Path.Combine(scratch.FullName, "alias"), target.Parent!.FullName);

            foreach (var path in new[]
                     {
                         Path.Combine(scratch.FullName, "alias", "child"),
                         nearPrefix
                     })
            {
                await using var factory = new CustomWebApplicationFactory("--Runner:WorkspaceRoot", path);
                using var client = factory.CreateClient();
                using var workspace = factory.Services.GetRequiredService<WorkspaceManager>().Create(999);
                var expected = path == nearPrefix ? nearPrefix : target.FullName;
                Assert.That(Directory.GetParent(workspace.Path)!.Parent!.FullName, Is.EqualTo(expected));
                Assert.That(await File.ReadAllTextAsync(existing), Is.EqualTo("preserve"));
            }
        }
        finally
        {
            scratch.Delete(true);
            Directory.Delete(nearPrefix, recursive: true);
        }
    }

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

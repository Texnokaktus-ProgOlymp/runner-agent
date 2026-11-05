using Texnokaktus.ProgOlymp.RunnerAgent.Workspace;

namespace Texnokaktus.ProgOlymp.RunnerAgent.Tests;

public sealed class RunnerOptionsTests
{
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("relative/path")]
    public void RejectsNonAbsoluteWorkspaceRoot(string root)
    {
        var options = new RunnerOptions { WorkspaceRoot = root };
        Assert.That(options.Validate, Throws.InvalidOperationException);
    }

    [Test]
    public void RejectsFilesystemRoot()
    {
        var options = new RunnerOptions { WorkspaceRoot = Path.GetPathRoot(Path.GetTempPath())! };
        Assert.That(options.Validate, Throws.InvalidOperationException);
    }

    [Test]
    public void RejectsMissingDirectoryWithoutCreatingIt()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var options = new RunnerOptions { WorkspaceRoot = root };
        Assert.That(options.Validate, Throws.InvalidOperationException);
        Assert.That(Directory.Exists(root), Is.False);
    }

    [Test]
    public void ValidatesWritableDirectoryWithoutChangingExistingContents()
    {
        var root = Directory.CreateTempSubdirectory("runner-options-");

        try
        {
            var existing = Path.Combine(root.FullName, "unrelated.txt");
            File.WriteAllText(existing, "preserve");
            var options = new RunnerOptions { WorkspaceRoot = root.FullName + Path.DirectorySeparatorChar };
            options.Validate();

            Assert.Multiple(() =>
                {
                    Assert.That(options.WorkspaceRoot, Is.EqualTo(root.FullName));
                    Assert.That(File.ReadAllText(existing), Is.EqualTo("preserve"));
                    Assert.That(Directory.GetFileSystemEntries(root.FullName), Has.Length.EqualTo(1));
                }
            );
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}

using Texnokaktus.ProgOlymp.RunnerAgent.Workspace;

namespace Texnokaktus.ProgOlymp.RunnerAgent.Tests;

public sealed class WorkspaceTests
{
    [Test]
    public void RepeatedJobIdsGetIndependentDirectoriesAndCleanupPreservesForeignFiles()
    {
        var root = Directory.CreateTempSubdirectory("runner-workspace-");

        try
        {
            var foreign = Path.Combine(root.FullName, "foreign.txt");
            File.WriteAllText(foreign, "preserve");
            var manager = new WorkspaceManager(root.FullName);
            string firstPath, secondPath;

            using (var first = manager.Create(8))
            using (var second = manager.Create(8))
            {
                firstPath = first.Path;
                secondPath = second.Path;
                File.WriteAllText(Path.Combine(first.Path, "output.txt"), "first");
                Assert.That(first.Path, Is.Not.EqualTo(second.Path));
                Assert.That(Directory.GetFiles(second.Path), Is.Empty);
            }

            Assert.That(Directory.Exists(firstPath), Is.False);
            Assert.That(Directory.Exists(secondPath), Is.False);
            Assert.That(File.ReadAllText(foreign), Is.EqualTo("preserve"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}

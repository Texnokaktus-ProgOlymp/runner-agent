namespace Texnokaktus.ProgOlymp.RunnerAgent.Workspace;

public sealed class JobWorkspace : IDisposable
{
    internal JobWorkspace(string path) => Path = path;

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}

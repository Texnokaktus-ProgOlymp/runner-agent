namespace Texnokaktus.ProgOlymp.RunnerAgent.Workspace;

public sealed class WorkspaceManager(string root)
{
    internal const string Marker = ".runner-agent-owned";
    private string OwnedRoot => Path.Combine(Path.GetFullPath(root), "runner-agent-jobs");

    private void EnsureOwnedRoot()
    {
        Directory.CreateDirectory(OwnedRoot);

        if ((File.GetAttributes(OwnedRoot) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The agent workspace namespace must not be a symbolic link.");
    }

    internal FileStream AcquireOwnership()
    {
        EnsureOwnedRoot();

        return new FileStream(
            Path.Combine(OwnedRoot, ".owner"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None
        );
    }

    internal void CleanupOrphans()
    {
        EnsureOwnedRoot();

        foreach (var path in Directory.EnumerateDirectories(OwnedRoot))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;

            var name = Path.GetFileName(path);
            var separator = name.LastIndexOf('-');

            if (separator < 1
             || !long.TryParse(
                    name.AsSpan(0, separator),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var id
                )
             || id <= 0
             || !Guid.TryParseExact(name.AsSpan(separator + 1), "N", out _)) continue;

            var marker = Path.Combine(path, Marker);

            if (File.Exists(marker)
             && (File.GetAttributes(marker) & FileAttributes.ReparsePoint) == 0
             && File.ReadAllText(marker) == "runner-agent-v1") Directory.Delete(path, true);
        }
    }

    public JobWorkspace Create(long jobId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(jobId);
        EnsureOwnedRoot();
        var path = Path.Combine(OwnedRoot, $"{jobId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);

        try
        {
            File.WriteAllText(Path.Combine(path, Marker), "runner-agent-v1");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Directory.Delete(path, recursive: true);

            throw;
        }

        return new JobWorkspace(path);
    }
}

namespace Texnokaktus.ProgOlymp.RunnerAgent.Workspace;

internal sealed class RunnerOptions
{
    public string WorkspaceRoot { get; set; } = "/var/lib/runner-agent";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(WorkspaceRoot) || !Path.IsPathFullyQualified(WorkspaceRoot))
            throw new InvalidOperationException("Runner:WorkspaceRoot must be an absolute path.");

        WorkspaceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(WorkspaceRoot));

        if (string.Equals(WorkspaceRoot, Path.GetPathRoot(WorkspaceRoot), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Runner:WorkspaceRoot must not be a filesystem root.");

        if (!Directory.Exists(WorkspaceRoot))
            throw new InvalidOperationException("Runner:WorkspaceRoot must be an existing directory.");

        // Check local access without creating a job workspace or cleaning existing contents.
        var probe = Path.Combine(WorkspaceRoot, $".runner-agent-probe-{Guid.NewGuid():N}");

        using var stream = new FileStream(
            probe,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            1,
            FileOptions.DeleteOnClose
        );

        stream.WriteByte(0);
    }
}

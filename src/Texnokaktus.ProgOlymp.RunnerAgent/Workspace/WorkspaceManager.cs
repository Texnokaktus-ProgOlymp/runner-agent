namespace Texnokaktus.ProgOlymp.RunnerAgent.Workspace;

public sealed class WorkspaceManager(string root)
{
    public JobWorkspace Create(long jobId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(jobId);
        var path = Path.Combine(Path.GetFullPath(root), "runner-agent-jobs", $"{jobId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);

        return new JobWorkspace(path);
    }
}

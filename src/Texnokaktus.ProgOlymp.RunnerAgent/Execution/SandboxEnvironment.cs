using System.Globalization;
using System.Runtime.InteropServices;
using Texnokaktus.ProgOlymp.RunnerAgent.Languages;
using Texnokaktus.ProgOlymp.RunnerAgent.Workspace;

namespace Texnokaktus.ProgOlymp.RunnerAgent.Execution;

internal sealed class SandboxEnvironment(
    IsolateSandbox sandbox,
    WorkspaceManager workspaces,
    ILanguageProvider language
)
    : ISandboxEnvironment, IDisposable
{
    private static readonly string[] RequiredControllers = ["cpu", "memory", "pids"];
    private FileStream? _ownership;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new InvalidOperationException("Sandbox requires Linux amd64.");

        if ((await File.ReadAllTextAsync("/proc/self/cgroup", cancellationToken)).Trim() != "0::/runner-agent-control")
            throw new InvalidOperationException("Sandbox requires a private cgroups v2 namespace.");

        _ownership = new FileStream(
            "/run/isolate/runner-agent.lock",
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None
        );

        const string root = "/sys/fs/cgroup/runner-agent-boxes";

        var controllers = (await File.ReadAllTextAsync(root + "/cgroup.subtree_control", cancellationToken))
           .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (!RequiredControllers.All(controllers.Contains))
            throw new InvalidOperationException("Sandbox requires delegated cpu, memory and pids controllers.");

        if (!File.Exists(language.CompilationCommand.Executable))
            throw new InvalidOperationException("Sandbox compiler is missing.");

        var cleaned = false;

        try
        {
            if (await IsolateSandbox.ControlAsync("--init", cancellationToken) != 0)
                throw new InvalidOperationException("Sandbox probe initialization failed.");

            const string group = root + "/box-0";

            foreach (var file in new[]
                     {
                         "cpu.stat",
                         "memory.peak",
                         "memory.max",
                         "memory.events",
                         "memory.swap.max",
                         "memory.oom.group",
                         "pids.max",
                         "pids.events",
                         "cgroup.kill",
                         "cgroup.events"
                     })
                if (!File.Exists(group + "/" + file))
                    throw new InvalidOperationException("Sandbox is missing a mandatory cgroup interface.");

            foreach (var setting in new[]
                     {
                         ("memory.swap.max", "0"),
                         ("memory.oom.group", "1"),
                         ("pids.max", "32")
                     })
            {
                var path = group + "/" + setting.Item1;
                await File.WriteAllTextAsync(path, setting.Item2, cancellationToken);

                if ((await File.ReadAllTextAsync(path, cancellationToken)).Trim() != setting.Item2)
                    throw new InvalidOperationException("Sandbox cgroup setting was not applied.");
            }

            _ = long.Parse(
                await File.ReadAllTextAsync(group + "/memory.peak", cancellationToken),
                CultureInfo.InvariantCulture
            );

            await File.WriteAllTextAsync(group + "/cgroup.kill", "1", cancellationToken);
        }
        finally
        {
            cleaned = await IsolateSandbox.ControlAsync("--cleanup", CancellationToken.None) == 0
                   && !Directory.Exists(root + "/box-0");
        }

        if (!cleaned)
            throw new SandboxCleanupException();

        using var workspace = workspaces.Create(1);

        var result = await sandbox.RunAsync(
                         "startup",
                         new SandboxCommand("/usr/bin/true", []),
                         "/usr/bin/true",
                         "probe",
                         null,
                         workspace.Path,
                         new ResourceLimits(1000, 2000, 64 * 1024 * 1024, 1024, 1024, 8),
                         null,
                         cancellationToken
                     );

        if (!result.EnvironmentReusable
         || result.Diagnostics.Count != 0
         || result.Execution?.Reason != TerminationReason.Success
         || result.Execution.CpuTimeMs is null
         || result.Execution.WallTimeMs is null
         || result.Execution.PeakMemoryBytes is null)
            throw new InvalidOperationException("Sandbox execution probe failed.");
    }

    public void Dispose() => _ownership?.Dispose();
}

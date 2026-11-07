using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Texnokaktus.ProgOlymp.RunnerAgent.Workspace;

[SupportedOSPlatform("linux")]
internal static partial class SandboxWorkspacePath
{
    internal static string Validate(string path)
    {
        var canonical = Resolve(path);

        foreach (var mount in new[]
                 {
                     "/usr",
                     "/bin",
                     "/lib",
                     "/lib64",
                     "/var/lib/runner-agent-dev"
                 })
        {
            // Optional/missing trees cannot contain an existing workspace. Startup separately checks the sandbox.
            if (!Directory.Exists(mount)) continue;

            var source = Resolve(mount);

            if (source == "/" || canonical == source || canonical.StartsWith(source + "/", StringComparison.Ordinal))
                throw new InvalidOperationException("Runner:WorkspaceRoot must be outside sandbox system mounts.");
        }

        return canonical;
    }

    private static string Resolve(string path)
    {
        var resolved = RealPath(path, 0);

        if (resolved == 0) throw new IOException("The workspace or sandbox mount path cannot be resolved.");

        try
        {
            return Marshal.PtrToStringUTF8(resolved)!;
        }
        finally
        {
            Free(resolved);
        }
    }

    [LibraryImport("libc", EntryPoint = "realpath", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint RealPath(string path, nint resolved);

    [LibraryImport("libc", EntryPoint = "free")]
    private static partial void Free(nint pointer);
}

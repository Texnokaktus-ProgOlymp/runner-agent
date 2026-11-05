using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Texnokaktus.ProgOlymp.RunnerAgent.Execution;

internal static partial class PosixSignals
{
    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    [SupportedOSPlatform("linux")]
    internal static partial int Send(int processId, int signal);
}

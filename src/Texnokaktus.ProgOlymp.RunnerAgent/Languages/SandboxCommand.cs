namespace Texnokaktus.ProgOlymp.RunnerAgent.Languages;

internal sealed record SandboxCommand(string Executable, IReadOnlyList<string> Arguments);

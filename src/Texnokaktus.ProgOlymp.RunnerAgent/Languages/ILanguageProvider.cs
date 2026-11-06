namespace Texnokaktus.ProgOlymp.RunnerAgent.Languages;

internal interface ILanguageProvider
{
    bool Supports(string language, string languageVersion);
    string SourceFileName { get; }
    string ExecutableFileName { get; }
    SandboxCommand CompilationCommand { get; }
    SandboxCommand ExecutionCommand { get; }
}

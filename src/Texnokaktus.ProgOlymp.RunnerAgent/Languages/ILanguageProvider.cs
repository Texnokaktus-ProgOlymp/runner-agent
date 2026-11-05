namespace Texnokaktus.ProgOlymp.RunnerAgent.Languages;

internal interface ILanguageProvider
{
    bool Supports(string language, string languageVersion);
    SandboxCommand CompilationCommand { get; }
    SandboxCommand ExecutionCommand { get; }
}

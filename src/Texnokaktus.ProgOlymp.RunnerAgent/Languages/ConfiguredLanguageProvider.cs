namespace Texnokaktus.ProgOlymp.RunnerAgent.Languages;

internal sealed class ConfiguredLanguageProvider : ILanguageProvider
{
    private readonly string _language;
    private readonly string _version;

    public ConfiguredLanguageProvider(LanguageProfile profile)
    {
        profile.Validate();
        _language = profile.Language;
        _version = profile.LanguageVersion;
        SourceFileName = profile.SourceFileName;
        ExecutableFileName = profile.ExecutableFileName;

        CompilationCommand = new(
            profile.Compilation.Executable,
            Array.AsReadOnly(profile.Compilation.Arguments.ToArray())
        );

        ExecutionCommand = new(profile.Execution.Executable, Array.AsReadOnly(profile.Execution.Arguments.ToArray()));
    }

    public bool Supports(string language, string languageVersion) =>
        language == _language && languageVersion == _version;

    public string SourceFileName { get; }
    public string ExecutableFileName { get; }
    public SandboxCommand CompilationCommand { get; }
    public SandboxCommand ExecutionCommand { get; }
}

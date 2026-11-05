namespace Texnokaktus.ProgOlymp.RunnerAgent.Languages;

internal sealed class CppLanguageProvider : ILanguageProvider
{
    public bool Supports(string language, string languageVersion) => language == "cpp" && languageVersion == "23";

    public SandboxCommand CompilationCommand { get; } = new(
        "/usr/bin/g++",
        Array.AsReadOnly(
            new[]
            {
                "-std=c++23",
                "-O2",
                "-pipe",
                "-DONLINE_JUDGE",
                "/box/source.cpp",
                "-o",
                "/box/solution"
            }
        )
    );

    public SandboxCommand ExecutionCommand { get; } = new("/box/solution", Array.Empty<string>());
}

namespace Texnokaktus.ProgOlymp.RunnerAgent.Languages;

internal sealed class LanguageProfile
{
    public string Language { get; set; } = "";
    public string LanguageVersion { get; set; } = "";
    public string SourceFileName { get; set; } = "";
    public string ExecutableFileName { get; set; } = "";
    public CommandProfile Compilation { get; set; } = new();
    public CommandProfile Execution { get; set; } = new();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Language)
         || string.IsNullOrWhiteSpace(LanguageVersion)
         || !ValidName(SourceFileName)
         || !ValidName(ExecutableFileName)
         || SourceFileName == ExecutableFileName
         || !ValidCommand(Compilation)
         || !ValidCommand(Execution))
            throw new InvalidOperationException("LanguageProfile must define one valid fixed language profile.");
    }

    private static bool ValidName(string name) => !string.IsNullOrWhiteSpace(name)
                                               && name is not ("." or ".." or "input.txt")
                                               && !name.Contains('/')
                                               && !name.Contains('\\')
                                               && !name.Contains('\0');

    private static bool ValidCommand(CommandProfile command) => command is not null
                                                             && !string.IsNullOrWhiteSpace(command.Executable)
                                                             && command.Executable.StartsWith('/')
                                                             && !command.Executable.Contains('\0')
                                                             && command.Arguments is not null
                                                             && command.Arguments.All(argument => argument is not null
                                                                                               && !argument.Contains(
                                                                                                      '\0'
                                                                                                  )
                                                                );

    internal sealed class CommandProfile
    {
        public string Executable { get; set; } = "";
        public string[] Arguments { get; set; } = [];
    }
}

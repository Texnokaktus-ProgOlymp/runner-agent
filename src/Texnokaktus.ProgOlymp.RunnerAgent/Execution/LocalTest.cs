namespace Texnokaktus.ProgOlymp.RunnerAgent.Execution;

public sealed record LocalTest(
    int TestId,
    string InputPath,
    IReadOnlyList<InfrastructureDiagnostic>? PreparationDiagnostics = null
);

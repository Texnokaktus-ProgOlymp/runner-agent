using Texnokaktus.ProgOlymp.RunnerAgent.IntegrationTests.Tools;

namespace Texnokaktus.ProgOlymp.RunnerAgent.IntegrationTests;

[SetUpFixture]
public sealed class SetupGlobal
{
    private string? _serviceName;
    private string? _workspace;
    private DirectoryInfo _directory = null!;

    public static CustomWebApplicationFactory Factory { get; private set; } = null!;
    public static string WorkspaceRoot { get; private set; } = null!;

    [OneTimeSetUp]
    public void SetUp()
    {
        _serviceName = Environment.GetEnvironmentVariable("SERVICE_NAME");
        _workspace = Environment.GetEnvironmentVariable("Runner__WorkspaceRoot");
        _directory = Directory.CreateTempSubdirectory("runner-integration-");
        WorkspaceRoot = _directory.FullName;
        Environment.SetEnvironmentVariable("SERVICE_NAME", "runner-agent-cpp-23-tests");
        Environment.SetEnvironmentVariable("Runner__WorkspaceRoot", WorkspaceRoot);
        Factory = new CustomWebApplicationFactory();
        using var client = Factory.CreateClient();
    }

    [OneTimeTearDown]
    public async Task TearDown()
    {
        try
        {
            if (Factory is not null)
                await Factory.DisposeAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICE_NAME", _serviceName);
            Environment.SetEnvironmentVariable("Runner__WorkspaceRoot", _workspace);
            _directory?.Delete(recursive: true);
        }
    }
}

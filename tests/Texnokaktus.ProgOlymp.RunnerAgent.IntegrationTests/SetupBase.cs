using Texnokaktus.ProgOlymp.RunnerAgent.IntegrationTests.Tools;

namespace Texnokaktus.ProgOlymp.RunnerAgent.IntegrationTests;

[NonParallelizable]
public abstract class SetupBase
{
    private string? _serviceName;
    private string? _workspace;

    protected static CustomWebApplicationFactory Factory => SetupGlobal.Factory;

    [SetUp]
    public void SetUp()
    {
        _serviceName = Environment.GetEnvironmentVariable("SERVICE_NAME");
        _workspace = Environment.GetEnvironmentVariable("Runner__WorkspaceRoot");
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("SERVICE_NAME", _serviceName);
        Environment.SetEnvironmentVariable("Runner__WorkspaceRoot", _workspace);
    }
}

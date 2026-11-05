using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Texnokaktus.ProgOlymp.RunnerAgent.IntegrationTests.Tools;

namespace Texnokaktus.ProgOlymp.RunnerAgent.IntegrationTests;

public sealed class HostConfigurationTests : SetupBase
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public async Task RejectsMissingOrBlankServiceName(string? value)
    {
        Environment.SetEnvironmentVariable("SERVICE_NAME", value);
        await using var factory = new CustomWebApplicationFactory();
        Assert.That(() => factory.CreateClient(), Throws.InvalidOperationException);
    }

    [Test]
    public void EnvironmentOverridesFileAndConfigurationIsStartupSnapshot()
    {
        var configuration = Factory.Services.GetRequiredService<IConfiguration>();
        Environment.SetEnvironmentVariable("Runner__WorkspaceRoot", "relative-path");
        Assert.That(configuration["Runner:WorkspaceRoot"], Is.EqualTo(SetupGlobal.WorkspaceRoot));
    }

    [Test]
    public async Task CommandLineCanOverrideEnvironment()
    {
        Environment.SetEnvironmentVariable("Runner__WorkspaceRoot", "relative-path");
        await using var factory = new CustomWebApplicationFactory("--Runner:WorkspaceRoot", SetupGlobal.WorkspaceRoot);
        using var client = factory.CreateClient();

        Assert.That(
            factory.Services.GetRequiredService<IConfiguration>()["Runner:WorkspaceRoot"],
            Is.EqualTo(SetupGlobal.WorkspaceRoot)
        );
    }

    [Test]
    public async Task IdleHostStartsServesPipelineAndStopsWithoutStorageOrIsolate()
    {
        IHostApplicationLifetime lifetime;

        await using (var factory = new CustomWebApplicationFactory())
        {
            using var client = factory.CreateClient();
            using var response = await client.GetAsync("/not-mapped");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            lifetime = factory.Services.GetRequiredService<IHostApplicationLifetime>();
            Assert.That(lifetime.ApplicationStarted.IsCancellationRequested, Is.True);
        }

        Assert.That(lifetime.ApplicationStopped.IsCancellationRequested, Is.True);
    }
}

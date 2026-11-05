using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Texnokaktus.ProgOlymp.RunnerAgent.IntegrationTests.Tools;

public sealed class CustomWebApplicationFactory(params string[] args) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseEnvironment("Testing").UseContentRoot(AppContext.BaseDirectory);

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuration => configuration.AddCommandLine(args));

        return base.CreateHost(builder);
    }
}

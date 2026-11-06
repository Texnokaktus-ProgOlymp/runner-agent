using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Texnokaktus.ProgOlymp.RunnerAgent.Execution;

namespace Texnokaktus.ProgOlymp.RunnerAgent.IntegrationTests.Tools;

public sealed class CustomWebApplicationFactory(params string[] args) : WebApplicationFactory<Program>
{
    public bool UseRealSandbox { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing").UseContentRoot(AppContext.BaseDirectory);

        if (!UseRealSandbox)
            builder.ConfigureTestServices(services => services.Replace(
                                              ServiceDescriptor.Singleton(Substitute.For<ISandboxEnvironment>())
                                          )
            );
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuration => configuration.AddCommandLine(args));

        return base.CreateHost(builder);
    }
}

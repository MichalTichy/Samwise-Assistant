using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Samwise.Infrastructure.AgentRuntimes.Agents;
using Samwise.Infrastructure.AgentRuntimes.Installers;
using Samwise.Infrastructure.CodexGateway.Catalogs;
using Samwise.Infrastructure.CodexGateway.Installers;
using Samwise.Logic.Agents;

namespace Samwise.Tests.Agents;

public sealed class AgentRuntimesInstallerTests
{
    [Fact]
    public void RegistersTheStandardOpenAiRuntime()
    {
        var services = new ServiceCollection();

        new AgentRuntimesInstaller().Install(
            services,
            new ConfigurationBuilder().Build(),
            isProduction: false);

        var registration = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(IAgentRuntime));
        Assert.Equal(typeof(OpenAiAgentRuntime), registration.ImplementationType);
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(IAgentModelCatalog));
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(IAgentToolCatalog));
    }

    [Fact]
    public void RegistersGatewaySpecificCatalogPortsOverOneClient()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AgentEndpoints:Intake:BaseAddress"] = "http://gateway.test/v1/",
                ["AgentEndpoints:Intake:ApiKey"] = "intake-key",
                ["AgentEndpoints:Planning:BaseAddress"] = "http://gateway.test/p/planning/v1/",
                ["AgentEndpoints:Planning:ApiKey"] = "planning-key",
                ["AgentEndpoints:Execution:BaseAddress"] = "http://gateway.test/p/execution/v1/",
                ["AgentEndpoints:Execution:ApiKey"] = "execution-key"
            })
            .Build();
        var services = new ServiceCollection();
        new AgentRuntimesInstaller().Install(services, configuration, isProduction: false);
        new CodexGatewayInstaller().Install(services, configuration, isProduction: false);
        using var provider = services.BuildServiceProvider();

        var toolCatalog = provider.GetRequiredService<IAgentToolCatalog>();
        var modelCatalog = provider.GetRequiredService<IAgentModelCatalog>();
        var planningClient = provider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(CodexGatewayCatalogClient.PlanningHttpClientName);

        Assert.IsType<CodexGatewayCatalogClient>(toolCatalog);
        Assert.Same(toolCatalog, modelCatalog);
        Assert.Equal("http://gateway.test/p/planning/v1/", planningClient.BaseAddress?.AbsoluteUri);
        Assert.Equal("Bearer planning-key", planningClient.DefaultRequestHeaders.Authorization?.ToString());
    }
}

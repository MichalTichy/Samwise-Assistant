using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Samwise.Infrastructure.AgentRuntimes.Agents;
using Samwise.Infrastructure.CodexGateway.Catalogs;
using Samwise.Logic.Agents;
using Shared.Infrastructure.IoC;

namespace Samwise.Infrastructure.CodexGateway.Installers;

public sealed class CodexGatewayInstaller : IInstaller
{
    public void Install(IServiceCollection services, IConfiguration configuration, bool isProduction)
    {
        services.AddSingleton<CodexGatewayCatalogClient>();
        services.AddSingleton<IAgentToolCatalog>(serviceProvider =>
            serviceProvider.GetRequiredService<CodexGatewayCatalogClient>());
        services.AddSingleton<IAgentModelCatalog>(serviceProvider =>
            serviceProvider.GetRequiredService<CodexGatewayCatalogClient>());

        services.AddHttpClient(CodexGatewayCatalogClient.PlanningHttpClientName, (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<AgentEndpointOptions>>().Value;
            CodexGatewayCatalogClient.ConfigureHttpClient(client, options.Planning, options.RequestTimeout);
        });
        services.AddHttpClient(CodexGatewayCatalogClient.IntakeHttpClientName, (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<AgentEndpointOptions>>().Value;
            CodexGatewayCatalogClient.ConfigureHttpClient(client, options.Intake, options.RequestTimeout);
        });
        services.AddHttpClient(CodexGatewayCatalogClient.ExecutionHttpClientName, (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<AgentEndpointOptions>>().Value;
            CodexGatewayCatalogClient.ConfigureHttpClient(client, options.Execution, options.RequestTimeout);
        });
        services.AddHttpClient(CodexGatewayCatalogClient.AutomaticApprovalHttpClientName, (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<AgentEndpointOptions>>().Value;
            CodexGatewayCatalogClient.ConfigureHttpClient(client, options.AutomaticApproval, options.RequestTimeout);
        });
        services.AddHttpClient(CodexGatewayCatalogClient.AutoApprovedExecutionHttpClientName, (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<AgentEndpointOptions>>().Value;
            CodexGatewayCatalogClient.ConfigureHttpClient(client, options.AutoApprovedExecution, options.RequestTimeout);
        });
    }
}

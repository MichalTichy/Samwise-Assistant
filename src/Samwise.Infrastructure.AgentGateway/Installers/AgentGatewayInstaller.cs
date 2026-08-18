using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Samwise.Infrastructure.AgentGateway.Agents;
using Shared.Infrastructure.IoC;

namespace Samwise.Infrastructure.AgentGateway.Installers;

public sealed class AgentGatewayInstaller : IInstaller
{
    public void Install(IServiceCollection services, IConfiguration configuration, bool isProduction)
    {
        services.Configure<CodexGatewayOptions>(configuration.GetSection(CodexGatewayOptions.SectionName));
        services.AddSingleton<ICodexGatewayClient, CodexGatewayClient>();

        services.AddHttpClient(CodexGatewayClient.PlanningHttpClientName, (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<CodexGatewayOptions>>().Value;
            CodexGatewayClient.ConfigureHttpClient(client, options.Planning, options.RequestTimeout);
        });
        services.AddHttpClient(CodexGatewayClient.IntakeHttpClientName, (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<CodexGatewayOptions>>().Value;
            CodexGatewayClient.ConfigureHttpClient(client, options.Intake, options.RequestTimeout);
        });
        services.AddHttpClient(CodexGatewayClient.ExecutionHttpClientName, (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<CodexGatewayOptions>>().Value;
            CodexGatewayClient.ConfigureHttpClient(client, options.Execution, options.RequestTimeout);
        });
    }
}

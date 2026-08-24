using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Samwise.Infrastructure.AgentGateway.Agents;
using Samwise.Logic.Agents;
using Shared.Infrastructure.IoC;

namespace Samwise.Infrastructure.AgentGateway.Installers;

public sealed class AgentGatewayInstaller : IInstaller
{
    public void Install(IServiceCollection services, IConfiguration configuration, bool isProduction)
    {
        services.Configure<AgentRuntimeOptions>(configuration.GetSection(AgentRuntimeOptions.SectionName));
        services.Configure<CodexGatewayOptions>(configuration.GetSection(CodexGatewayOptions.SectionName));

        var provider = configuration[$"{AgentRuntimeOptions.SectionName}:Provider"] ?? "Gateway";
        if (string.Equals(provider, "Demo", StringComparison.OrdinalIgnoreCase))
        {
            if (isProduction)
            {
                throw new InvalidOperationException("The demo agent runtime is available only outside Production.");
            }

            services.AddSingleton<IAgentRuntime, DemoAgentRuntime>();
        }
        else if (string.Equals(provider, "Gateway", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IAgentRuntime, GatewayAgentRuntime>();
        }
        else
        {
            throw new InvalidOperationException($"Unknown agent runtime provider '{provider}'.");
        }

        services.AddHttpClient(GatewayAgentRuntime.PlanningHttpClientName, (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<CodexGatewayOptions>>().Value;
            GatewayAgentRuntime.ConfigureHttpClient(client, options.Planning, options.RequestTimeout);
        });
        services.AddHttpClient(GatewayAgentRuntime.IntakeHttpClientName, (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<CodexGatewayOptions>>().Value;
            GatewayAgentRuntime.ConfigureHttpClient(client, options.Intake, options.RequestTimeout);
        });
        services.AddHttpClient(GatewayAgentRuntime.ExecutionHttpClientName, (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<CodexGatewayOptions>>().Value;
            GatewayAgentRuntime.ConfigureHttpClient(client, options.Execution, options.RequestTimeout);
        });
    }
}

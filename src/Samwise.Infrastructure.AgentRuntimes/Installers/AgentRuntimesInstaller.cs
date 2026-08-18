using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Samwise.Infrastructure.AgentRuntimes.Agents;
using Samwise.Logic.Agents;
using Shared.Infrastructure.IoC;

namespace Samwise.Infrastructure.AgentRuntimes.Installers;

public sealed class AgentRuntimesInstaller : IInstaller
{
    public void Install(IServiceCollection services, IConfiguration configuration, bool isProduction)
    {
        services.Configure<AgentRuntimeOptions>(configuration.GetSection(AgentRuntimeOptions.SectionName));

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
    }
}

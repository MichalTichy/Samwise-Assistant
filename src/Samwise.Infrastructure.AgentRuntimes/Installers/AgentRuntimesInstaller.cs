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
        services.Configure<AgentEndpointOptions>(configuration.GetSection(AgentEndpointOptions.SectionName));
        services.AddSingleton<IAgentRuntime, OpenAiAgentRuntime>();
    }
}

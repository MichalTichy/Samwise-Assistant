using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Samwise.Logic.Prompts;
using Samwise.Logic.Agents;
using Shared.Infrastructure.IoC;

namespace Samwise.Logic.Installers;

public sealed class LogicInstaller : IInstaller
{
    public void Install(IServiceCollection services, IConfiguration configuration, bool isProduction)
    {
        services.AddMediatR(options => options.RegisterServicesFromAssembly(typeof(LogicInstaller).Assembly));
        services.AddSingleton<IPromptRenderer, PromptRenderer>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AgentInvocationRecorder>();
    }
}

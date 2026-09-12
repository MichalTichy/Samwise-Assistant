using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Samwise.Logic.Agents.Services;
using Samwise.Logic.Inbox.Services;
using Samwise.Logic.Inputs.Email.Services;
using Samwise.Logic.Prompts;
using Samwise.Logic.WorkItems.Services;
using Shared.Infrastructure.IoC;

namespace Samwise.Logic.Installers;

public sealed class LogicInstaller : IInstaller
{
    public void Install(IServiceCollection services, IConfiguration configuration, bool isProduction)
    {
        services.AddMediatR(options => options.RegisterServicesFromAssembly(typeof(LogicInstaller).Assembly));
        services.AddScoped<IAgentInvocationService, AgentInvocationService>();
        services.AddScoped<IInboundEventClassificationService, InboundEventClassificationService>();
        services.AddScoped<IInboundEventProcessingService, InboundEventProcessingService>();
        services.AddScoped<IInboundEventCaptureService, InboundEventCaptureService>();
        services.AddSingleton<IEmailAccountProcessingLock, EmailAccountProcessingLock>();
        services.AddScoped<IWorkItemPlanningService, WorkItemPlanningService>();
        services.AddScoped<IWorkItemExecutionService, WorkItemExecutionService>();
        services.AddSingleton<IPromptRenderer, PromptRenderer>();
        services.AddSingleton(TimeProvider.System);
    }
}

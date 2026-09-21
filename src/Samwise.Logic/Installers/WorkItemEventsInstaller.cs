using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Samwise.Logic.WorkItems.Services;
using Shared.Infrastructure.IoC;

namespace Samwise.Logic.Installers;

public sealed class WorkItemEventsInstaller : IInstaller
{
    public void Install(IServiceCollection services, IConfiguration configuration, bool isProduction) =>
        services.AddScoped<IWorkItemEventPublisher, WorkItemEventPublisher>();
}

using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Samwise.Inputs.Todoist.Ingestion;
using Shared.Infrastructure.IoC;

namespace Samwise.Inputs.Todoist.Installers;

public sealed class TodoistInputModuleInstaller : IInstaller
{
    public void Install(IServiceCollection services, IConfiguration configuration, bool isProduction)
    {
        services.AddMediatR(options =>
            options.RegisterServicesFromAssembly(typeof(TodoistInputModuleInstaller).Assembly));
        services.AddSingleton<ITodoistInputProcessingLock, TodoistInputProcessingLock>();
        services.AddScoped<Samwise.Inputs.Todoist.Tasks.ITodoistTaskSyncService, Samwise.Inputs.Todoist.Tasks.TodoistTaskSyncService>();
        services.AddScoped<Samwise.Logic.Tasks.IExternalTaskRunGate, Samwise.Inputs.Todoist.Tasks.TodoistTaskRunGate>();
    }
}

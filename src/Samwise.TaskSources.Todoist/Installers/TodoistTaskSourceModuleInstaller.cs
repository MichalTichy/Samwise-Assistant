using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Samwise.TaskSources.Todoist.Synchronization;
using Shared.Infrastructure.IoC;

namespace Samwise.TaskSources.Todoist.Installers;

public sealed class TodoistTaskSourceModuleInstaller : IInstaller
{
    public void Install(IServiceCollection services, IConfiguration configuration, bool isProduction)
    {
        services.AddMediatR(options =>
            options.RegisterServicesFromAssembly(typeof(TodoistTaskSourceModuleInstaller).Assembly));
        services.AddSingleton<ITodoistSynchronizationLock, TodoistSynchronizationLock>();
        services.AddScoped<Samwise.TaskSources.Todoist.Synchronization.ITodoistTaskSyncService, Samwise.TaskSources.Todoist.Synchronization.TodoistTaskSyncService>();
        services.AddScoped<Samwise.Logic.Tasks.IExternalTaskRunGate, Samwise.TaskSources.Todoist.Synchronization.TodoistTaskRunGate>();
    }
}

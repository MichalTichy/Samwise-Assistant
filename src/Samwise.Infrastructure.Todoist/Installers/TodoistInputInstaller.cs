using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Samwise.Infrastructure.Todoist.Api;
using Samwise.Infrastructure.Todoist.Authentication;
using Samwise.Infrastructure.Todoist.Polling;
using Samwise.Infrastructure.Todoist.Readers;
using Samwise.Inputs.Todoist.Authentication;
using Samwise.Inputs.Todoist.Ingestion;
using Shared.Infrastructure.IoC;

namespace Samwise.Infrastructure.Todoist.Installers;

public sealed class TodoistInputInstaller : IInstaller
{
    public void Install(IServiceCollection services, IConfiguration configuration, bool isProduction)
    {
        services.AddHttpClient<TodoistApiClient>(client =>
            client.BaseAddress = new Uri(TodoistApiClient.BaseAddress));
        services.AddSingleton<ITodoistCredentialProtector, DataProtectionTodoistCredentialProtector>();
        services.AddScoped<ITodoistTaskReader, TodoistRestTaskReader>();
        services.AddHostedService<TodoistPollingBackgroundService>();
    }
}

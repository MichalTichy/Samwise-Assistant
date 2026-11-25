using Samwise.TaskSources.Todoist.Configuration;
using Shared.Infrastructure.Initializer;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.TaskSources.Todoist.Initialization;

public sealed class TodoistTaskSourceConfigurationInitializer(
    IRepository<TodoistTaskSourceConfiguration> repository) : InitializerBase
{
    public override int Priority => 0;

    public override InitializerTrigger Trigger => InitializerTrigger.OnStartup;

    public override bool RunOnlyInLeaderInstance => false;

    protected override async Task RunInitializationLogicAsync()
    {
        if (await repository.GetByIdAsync(TodoistTaskSourceConfiguration.DocumentId) is null)
        {
            await repository.AddAsync(new TodoistTaskSourceConfiguration());
        }
    }
}

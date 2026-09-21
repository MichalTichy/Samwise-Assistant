using Samwise.Inputs.Todoist.Configuration;
using Shared.Infrastructure.Initializer;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Inputs.Todoist.Initialization;

public sealed class TodoistInputConfigurationInitializer(
    IRepository<TodoistInputConfiguration> repository) : InitializerBase
{
    public override int Priority => 0;

    public override InitializerTrigger Trigger => InitializerTrigger.OnStartup;

    public override bool RunOnlyInLeaderInstance => false;

    protected override async Task RunInitializationLogicAsync()
    {
        if (await repository.GetByIdAsync(TodoistInputConfiguration.DocumentId) is null)
        {
            await repository.AddAsync(new TodoistInputConfiguration());
        }
    }
}

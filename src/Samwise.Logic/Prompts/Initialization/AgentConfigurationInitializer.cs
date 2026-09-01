using Samwise.Domain.Agents;
using Shared.Infrastructure.Initializer;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Prompts.Initialization;

public sealed class AgentConfigurationInitializer(
    IRepository<AgentConfiguration> repository,
    TimeProvider timeProvider) : InitializerBase
{
    public override int Priority => 0;

    public override InitializerTrigger Trigger => InitializerTrigger.OnStartup;

    public override bool RunOnlyInLeaderInstance => false;

    protected override async Task RunInitializationLogicAsync()
    {
        if (await repository.GetByIdAsync(AgentConfiguration.DocumentId) is null)
        {
            await repository.AddAsync(DefaultAgentConfiguration.Create(timeProvider.GetUtcNow()));
        }
    }
}

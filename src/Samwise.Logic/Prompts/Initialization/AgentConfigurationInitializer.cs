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
        var configuration = await repository.GetByIdAsync(AgentConfiguration.DocumentId);
        if (configuration is null)
        {
            await repository.AddAsync(DefaultAgentConfiguration.Create(timeProvider.GetUtcNow()));
            return;
        }

        var defaults = DefaultAgentConfiguration.Create(timeProvider.GetUtcNow());
        var changed = false;
        if (string.IsNullOrWhiteSpace(configuration.AutomaticApproval.Prompt))
        {
            configuration.AutomaticApproval.Prompt = defaults.AutomaticApproval.Prompt;
            configuration.AutomaticApproval.UpdatedAt = defaults.AutomaticApproval.UpdatedAt;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(configuration.AutoApprovedExecution.Prompt))
        {
            configuration.AutoApprovedExecution.Prompt = defaults.AutoApprovedExecution.Prompt;
            configuration.AutoApprovedExecution.UpdatedAt = defaults.AutoApprovedExecution.UpdatedAt;
            changed = true;
        }

        if (changed)
        {
            await repository.UpdateAsync(configuration);
        }
    }
}

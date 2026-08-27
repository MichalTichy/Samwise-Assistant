using Samwise.Domain.Prompts;
using Shared.Infrastructure.Initializer;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Prompts.Initialization;

public sealed class PromptTemplateInitializer(
    IRepository<PromptTemplate> repository,
    TimeProvider timeProvider) : InitializerBase
{
    public override int Priority => 0;

    public override InitializerTrigger Trigger => InitializerTrigger.OnStartup;

    public override bool RunOnlyInLeaderInstance => false;

    protected override async Task RunInitializationLogicAsync()
    {
        foreach (var template in DefaultPromptTemplates.Create(timeProvider.GetUtcNow()))
        {
            if (await repository.GetByIdAsync(template.Id) is null)
            {
                await repository.AddAsync(template);
            }
        }
    }
}

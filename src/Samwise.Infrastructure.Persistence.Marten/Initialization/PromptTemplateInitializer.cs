using Marten;
using Samwise.Domain.Prompts;
using Samwise.Logic.Time;
using Shared.Infrastructure.Initializer;

namespace Samwise.Infrastructure.Persistence.Marten.Initialization;

public sealed class PromptTemplateInitializer(IDocumentStore store, IClock clock) : InitializerBase
{
    public override int Priority => 0;

    public override InitializerTrigger Trigger => InitializerTrigger.OnStartup;

    public override bool RunOnlyInLeaderInstance => false;

    protected override async Task RunInitializationLogicAsync()
    {
        await using var session = store.LightweightSession();
        foreach (var template in DefaultPromptTemplates.Create(clock.UtcNow))
        {
            if (await session.LoadAsync<PromptTemplate>(template.Id) is null)
            {
                session.Store(template);
            }
        }

        await session.SaveChangesAsync();
    }
}

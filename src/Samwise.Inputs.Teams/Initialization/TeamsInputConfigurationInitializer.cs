using Samwise.Inputs.Teams.Configuration;
using Shared.Infrastructure.Initializer;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Inputs.Teams.Initialization;

public sealed class TeamsInputConfigurationInitializer(
    IRepository<TeamsInputConfiguration> repository) : InitializerBase
{
    public override int Priority => 0;

    public override InitializerTrigger Trigger => InitializerTrigger.OnStartup;

    public override bool RunOnlyInLeaderInstance => false;

    protected override async Task RunInitializationLogicAsync()
    {
        if (await repository.GetByIdAsync(TeamsInputConfiguration.DocumentId) is null)
        {
            await repository.AddAsync(new TeamsInputConfiguration());
        }
    }
}

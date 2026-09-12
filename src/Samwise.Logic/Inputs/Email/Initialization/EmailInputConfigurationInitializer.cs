using Samwise.Domain.Inputs.Email;
using Shared.Infrastructure.Initializer;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Inputs.Email.Initialization;

public sealed class EmailInputConfigurationInitializer(
    IRepository<EmailInputConfiguration> repository) : InitializerBase
{
    public override int Priority => 0;

    public override InitializerTrigger Trigger => InitializerTrigger.OnStartup;

    public override bool RunOnlyInLeaderInstance => false;

    protected override async Task RunInitializationLogicAsync()
    {
        if (await repository.GetByIdAsync(EmailInputConfiguration.DocumentId) is null)
        {
            await repository.AddAsync(new EmailInputConfiguration());
        }
    }
}

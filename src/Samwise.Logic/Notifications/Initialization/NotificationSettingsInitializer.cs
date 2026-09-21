using Samwise.Domain.Notifications;
using Shared.Infrastructure.Initializer;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Notifications.Initialization;

public sealed class NotificationSettingsInitializer(
    IRepository<NotificationSettings> repository) : InitializerBase
{
    public override int Priority => 0;

    public override InitializerTrigger Trigger => InitializerTrigger.OnStartup;

    public override bool RunOnlyInLeaderInstance => false;

    protected override async Task RunInitializationLogicAsync()
    {
        if (await repository.GetByIdAsync(NotificationSettings.DocumentId) is null)
        {
            await repository.AddAsync(new NotificationSettings());
        }
    }
}

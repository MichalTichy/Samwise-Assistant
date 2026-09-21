using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Samwise.Logic.Notifications.Services;
using Shared.Infrastructure.IoC;

namespace Samwise.Logic.Installers;

public sealed class NotificationsInstaller : IInstaller
{
    public void Install(IServiceCollection services, IConfiguration configuration, bool isProduction)
    {
        // MediatR handlers for the notification feature are registered by LogicInstaller's assembly scan.
        services.AddScoped<IWorkItemAttentionNotifier, WorkItemAttentionNotifier>();
        services.AddScoped<IPushNotificationService, PushNotificationService>();
    }
}

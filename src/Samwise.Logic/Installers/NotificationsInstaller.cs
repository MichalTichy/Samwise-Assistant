using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Samwise.Logic.Notifications.Services;
using Shared.Infrastructure.IoC;

namespace Samwise.Logic.Installers;

public sealed class NotificationsInstaller : IInstaller
{
    public void Install(IServiceCollection services, IConfiguration configuration, bool isProduction)
    {
        // WorkItemPushNotificationHandler is registered by LogicInstaller's MediatR assembly scan.
        services.AddScoped<IPushNotificationService, PushNotificationService>();
    }
}

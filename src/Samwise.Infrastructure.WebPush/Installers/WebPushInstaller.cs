using Lib.Net.Http.WebPush;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Samwise.Infrastructure.WebPush.Keys;
using Samwise.Infrastructure.WebPush.Sending;
using Samwise.Logic.Notifications.Services;
using Shared.Infrastructure.IoC;

namespace Samwise.Infrastructure.WebPush.Installers;

public sealed class WebPushInstaller : IInstaller
{
    public void Install(IServiceCollection services, IConfiguration configuration, bool isProduction)
    {
        services.Configure<WebPushOptions>(configuration.GetSection(WebPushOptions.SectionName));
        services.AddHttpClient<PushServiceClient>();
        services.AddSingleton<VapidKeyProtector>();
        services.AddScoped<WebPushNotificationSender>();
        services.AddScoped<IPushNotificationSender>(provider => provider.GetRequiredService<WebPushNotificationSender>());
        services.AddScoped<IPushApplicationKeyProvider>(provider => provider.GetRequiredService<WebPushNotificationSender>());
    }
}

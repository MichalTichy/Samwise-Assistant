using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.Configuration;
using LumexUI.Extensions;
using Samwise.Web.Endpoints.ManualInput;
using Samwise.Web.InboxProcessing;
using Shared.Infrastructure.IoC;

namespace Samwise.Web.Installers;

public sealed class WebInstaller : IInstaller
{
    public void Install(IServiceCollection services, IConfiguration configuration, bool isProduction)
    {
        services.AddRazorComponents()
            .AddInteractiveServerComponents();
        services.AddLumexServices();
        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();
        services.Configure<ManualInputApiOptions>(
            configuration.GetSection(ManualInputApiOptions.SectionName));
        services.AddHostedService<InboundEventProcessingBackgroundService>();
    }
}

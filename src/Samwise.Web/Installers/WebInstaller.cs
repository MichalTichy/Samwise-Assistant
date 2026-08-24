using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Samwise.Web.Components.Account;
using Shared.Infrastructure.IoC;

namespace Samwise.Web.Installers;

public sealed class WebInstaller : IInstaller
{
    public void Install(IServiceCollection services, IConfiguration configuration, bool isProduction)
    {
        services.AddRazorComponents()
            .AddInteractiveServerComponents();
        services.AddCascadingAuthenticationState();
        services.AddScoped<IdentityRedirectManager>();
        services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
    }
}

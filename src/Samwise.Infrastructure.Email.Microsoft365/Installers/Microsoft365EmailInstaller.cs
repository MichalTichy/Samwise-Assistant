using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Samwise.Infrastructure.Email.Imap.Authentication;
using Samwise.Infrastructure.Email.Microsoft365.Authentication;
using Samwise.Logic.Inputs.Email;
using Shared.Infrastructure.IoC;

namespace Samwise.Infrastructure.Email.Microsoft365.Installers;

public sealed class Microsoft365EmailInstaller : IInstaller
{
    public void Install(IServiceCollection services, IConfiguration configuration, bool isProduction)
    {
        services.Configure<Microsoft365EmailOptions>(
            configuration.GetSection(Microsoft365EmailOptions.SectionName));
        services.AddSingleton<IMicrosoftEmailAuthorizationService, MicrosoftEmailAuthorizationService>();
        services.AddSingleton<IMicrosoftEmailAccessTokenProvider, MicrosoftEmailAccessTokenProvider>();
        services.AddSingleton<IEmailAuthenticationHandler, Microsoft365EmailAuthenticationHandler>();
    }
}

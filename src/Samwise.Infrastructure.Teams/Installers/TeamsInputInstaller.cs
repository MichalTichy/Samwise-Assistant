using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Samwise.Infrastructure.Teams.Authentication;
using Samwise.Infrastructure.Teams.Graph;
using Samwise.Infrastructure.Teams.Readers;
using Samwise.Infrastructure.Teams.Polling;
using Samwise.Inputs.Teams.Authentication;
using Samwise.Inputs.Teams.Ingestion;
using Shared.Infrastructure.IoC;

namespace Samwise.Infrastructure.Teams.Installers;

public sealed class TeamsInputInstaller : IInstaller
{
    public void Install(IServiceCollection services, IConfiguration configuration, bool isProduction)
    {
        services.AddHttpClient<TeamsGraphClient>(client =>
            client.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/"));
        services.AddSingleton<ITeamsCredentialProtector, DataProtectionTeamsCredentialProtector>();
        services.AddSingleton<TeamsGraphTokenProvider>();
        services.AddSingleton<ITeamsAuthorizationService>(provider =>
            provider.GetRequiredService<TeamsGraphTokenProvider>());
        services.AddScoped<ITeamsInputReader, MicrosoftGraphTeamsInputReader>();
        services.AddHostedService<TeamsPollingBackgroundService>();
    }
}

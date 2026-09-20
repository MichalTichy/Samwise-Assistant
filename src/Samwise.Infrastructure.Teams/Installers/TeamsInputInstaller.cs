using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Samwise.Infrastructure.Teams.Authentication;
using Samwise.Infrastructure.Teams.Configuration;
using Samwise.Infrastructure.Teams.Graph;
using Samwise.Infrastructure.Teams.Readers;
using Samwise.Infrastructure.Teams.Polling;
using Samwise.Inputs.Teams;
using Shared.Infrastructure.IoC;

namespace Samwise.Infrastructure.Teams.Installers;

public sealed class TeamsInputInstaller : IInstaller
{
    public void Install(IServiceCollection services, IConfiguration configuration, bool isProduction)
    {
        services.AddOptions<TeamsMicrosoftGraphOptions>()
            .Bind(configuration.GetSection(TeamsMicrosoftGraphOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ClientId),
                "TeamsInput:MicrosoftGraph:ClientId is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.TenantId),
                "TeamsInput:MicrosoftGraph:TenantId is required.")
            .Validate(options => options.GraphBaseUrl.IsAbsoluteUri,
                "TeamsInput:MicrosoftGraph:GraphBaseUrl must be absolute.")
            .ValidateOnStart();
        services.AddHttpClient<TeamsGraphClient>();
        services.AddSingleton<TeamsGraphTokenProvider>();
        services.AddSingleton<ITeamsAuthorizationService>(provider =>
            provider.GetRequiredService<TeamsGraphTokenProvider>());
        services.AddScoped<ITeamsInputReader, MicrosoftGraphTeamsInputReader>();
        services.AddHostedService<TeamsPollingBackgroundService>();
    }
}

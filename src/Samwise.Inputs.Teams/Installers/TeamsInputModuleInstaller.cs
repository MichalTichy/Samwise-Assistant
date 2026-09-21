using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Samwise.Inputs.Teams.Ingestion;
using Shared.Infrastructure.IoC;

namespace Samwise.Inputs.Teams.Installers;

public sealed class TeamsInputModuleInstaller : IInstaller
{
    public void Install(IServiceCollection services, IConfiguration configuration, bool isProduction)
    {
        services.AddMediatR(options =>
            options.RegisterServicesFromAssembly(typeof(TeamsInputModuleInstaller).Assembly));
        services.AddSingleton<ITeamsInputProcessingLock, TeamsInputProcessingLock>();
    }
}

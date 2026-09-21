using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Samwise.Inputs.Manual.Processing;
using Shared.Infrastructure.IoC;

namespace Samwise.Inputs.Manual.Installers;

public sealed class ManualInputInstaller : IInstaller
{
    public void Install(IServiceCollection services, IConfiguration configuration, bool isProduction)
    {
        services.AddMediatR(options =>
            options.RegisterServicesFromAssembly(typeof(ManualInputInstaller).Assembly));
        services.AddSingleton<IManualInputProcessingQueue, ManualInputProcessingQueue>();
        services.AddHostedService<ManualInputProcessingBackgroundService>();
    }
}

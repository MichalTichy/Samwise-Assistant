using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Samwise.Inputs.Email.Services;
using Shared.Infrastructure.IoC;

namespace Samwise.Inputs.Email.Installers;

public sealed class EmailInputInstaller : IInstaller
{
    public void Install(IServiceCollection services, IConfiguration configuration, bool isProduction)
    {
        services.AddMediatR(options =>
            options.RegisterServicesFromAssembly(typeof(EmailInputInstaller).Assembly));
        services.AddSingleton<IEmailAccountProcessingLock, EmailAccountProcessingLock>();
    }
}

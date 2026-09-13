using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Samwise.Infrastructure.Email.Imap.Authentication;
using Samwise.Infrastructure.Email.Imap.Installers;
using Samwise.Infrastructure.Email.Microsoft365.Installers;

namespace Samwise.Tests.Architecture;

public sealed class EmailInfrastructureInstallerTests
{
    [Fact]
    public void PasswordAndMicrosoft365AuthenticationAreRegisteredBySeparateProjects()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        new EmailImapInstaller().Install(services, configuration, isProduction: false);
        new Microsoft365EmailInstaller().Install(services, configuration, isProduction: false);

        Assert.Equal(
            2,
            services.Count(descriptor =>
                descriptor.ServiceType == typeof(IEmailAuthenticationHandler)));
    }
}

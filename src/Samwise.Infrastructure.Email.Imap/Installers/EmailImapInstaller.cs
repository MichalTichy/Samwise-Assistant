using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Samwise.Infrastructure.Email.Imap.Credentials;
using Samwise.Infrastructure.Email.Imap.Mailboxes;
using Samwise.Infrastructure.Email.Imap.Polling;
using Samwise.Logic.Inputs.Email;
using Shared.Infrastructure.IoC;

namespace Samwise.Infrastructure.Email.Imap.Installers;

public sealed class EmailImapInstaller : IInstaller
{
    public void Install(IServiceCollection services, IConfiguration configuration, bool isProduction)
    {
        services.AddSingleton<IEmailCredentialProtector, DataProtectionEmailCredentialProtector>();
        services.AddScoped<IEmailInboxReader, MailKitEmailInboxReader>();
        services.AddHostedService<EmailPollingBackgroundService>();
    }
}

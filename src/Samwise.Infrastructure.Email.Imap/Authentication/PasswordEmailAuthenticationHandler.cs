using MailKit.Net.Imap;
using Samwise.Inputs.Email;

namespace Samwise.Infrastructure.Email.Imap.Authentication;

internal sealed class PasswordEmailAuthenticationHandler(
    IEmailCredentialProtector credentialProtector) : IEmailAuthenticationHandler
{
    public EmailAuthenticationMethod Method => EmailAuthenticationMethod.Password;

    public Task AuthenticateAsync(
        ImapClient client,
        EmailAccount account,
        CancellationToken cancellationToken) => client.AuthenticateAsync(
            account.Username,
            credentialProtector.Unprotect(account.ProtectedPassword),
            cancellationToken);
}

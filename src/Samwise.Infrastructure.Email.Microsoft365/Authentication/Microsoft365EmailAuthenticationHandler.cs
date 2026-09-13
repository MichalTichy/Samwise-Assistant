using MailKit.Net.Imap;
using MailKit.Security;
using Samwise.Domain.Inputs.Email;
using Samwise.Infrastructure.Email.Imap.Authentication;

namespace Samwise.Infrastructure.Email.Microsoft365.Authentication;

internal sealed class Microsoft365EmailAuthenticationHandler(
    IMicrosoftEmailAccessTokenProvider accessTokenProvider) : IEmailAuthenticationHandler
{
    public EmailAuthenticationMethod Method => EmailAuthenticationMethod.Microsoft365OAuth;

    public async Task AuthenticateAsync(
        ImapClient client,
        EmailAccount account,
        CancellationToken cancellationToken)
    {
        var accessToken = await accessTokenProvider.GetAccessTokenAsync(account, cancellationToken);
        await client.AuthenticateAsync(
            new SaslMechanismOAuth2(account.Username, accessToken),
            cancellationToken);
    }
}

using MailKit.Net.Imap;
using Samwise.Inputs.Email;

namespace Samwise.Infrastructure.Email.Imap.Authentication;

public interface IEmailAuthenticationHandler
{
    EmailAuthenticationMethod Method { get; }

    Task AuthenticateAsync(
        ImapClient client,
        EmailAccount account,
        CancellationToken cancellationToken);
}

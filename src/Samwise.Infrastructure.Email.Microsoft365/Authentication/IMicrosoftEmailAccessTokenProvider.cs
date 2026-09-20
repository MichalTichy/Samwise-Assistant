using Samwise.Inputs.Email;

namespace Samwise.Infrastructure.Email.Microsoft365.Authentication;

internal interface IMicrosoftEmailAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(EmailAccount account, CancellationToken cancellationToken);
}

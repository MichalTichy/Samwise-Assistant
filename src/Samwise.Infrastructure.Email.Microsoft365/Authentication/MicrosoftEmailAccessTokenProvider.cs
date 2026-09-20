using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Samwise.Inputs.Email;

namespace Samwise.Infrastructure.Email.Microsoft365.Authentication;

internal sealed class MicrosoftEmailAccessTokenProvider(
    IOptions<Microsoft365EmailOptions> options,
    IEmailCredentialProtector credentialProtector) : IMicrosoftEmailAccessTokenProvider
{
    public async Task<string> GetAccessTokenAsync(
        EmailAccount account,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ClientId)
            || string.IsNullOrWhiteSpace(settings.TenantId))
        {
            throw new InvalidOperationException(
                "Microsoft 365 email sign-in is not configured for this Samwise instance.");
        }

        if (string.IsNullOrWhiteSpace(account.ProtectedOAuthTokenCache))
        {
            throw new InvalidOperationException(
                "This Microsoft 365 account is not connected. Reconnect it in Configuration > Inputs > Emails.");
        }

        var application = PublicClientApplicationBuilder
            .Create(settings.ClientId)
            .WithAuthority($"https://login.microsoftonline.com/{settings.TenantId}")
            .Build();
        var serializedCache = Convert.FromBase64String(
            credentialProtector.Unprotect(account.ProtectedOAuthTokenCache));
        var tokenCache = new MicrosoftTokenCacheState(serializedCache);
        MicrosoftTokenCachePersistence.Attach(application.UserTokenCache, tokenCache);

        var availableAccounts = await application.GetAccountsAsync();
        var microsoftAccount = availableAccounts.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.HomeAccountId.Identifier,
                    account.OAuthHomeAccountId,
                    StringComparison.Ordinal))
            ?? availableAccounts.SingleOrDefault();
        if (microsoftAccount is null)
        {
            throw new InvalidOperationException(
                "The saved Microsoft 365 session is unavailable. Reconnect the account.");
        }

        AuthenticationResult result;
        try
        {
            result = await application
                .AcquireTokenSilent(Microsoft365EmailScopes.Values, microsoftAccount)
                .ExecuteAsync(cancellationToken);
        }
        catch (MsalUiRequiredException exception)
        {
            throw new InvalidOperationException(
                "Microsoft 365 requires you to sign in again. Reconnect the account in Configuration > Inputs > Emails.",
                exception);
        }

        account.ProtectedOAuthTokenCache = credentialProtector.Protect(
            Convert.ToBase64String(tokenCache.Bytes ?? serializedCache));
        account.OAuthHomeAccountId = result.Account.HomeAccountId.Identifier;
        return result.AccessToken;
    }
}

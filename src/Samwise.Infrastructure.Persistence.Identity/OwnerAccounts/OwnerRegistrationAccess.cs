using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Samwise.Infrastructure.Persistence.Identity.OwnerAccounts;

public sealed class OwnerRegistrationAccess(IOptions<OwnerAccountOptions> options, OwnerAccountReader reader)
{
    public async Task<bool> IsAvailableAsync(string? token, CancellationToken cancellationToken = default) =>
        MatchesToken(options.Value.RegistrationToken, token) && await reader.ReadAsync(cancellationToken) is null;

    public static bool MatchesToken(string? configured, string? supplied)
    {
        if (string.IsNullOrWhiteSpace(configured) || configured.Length < 32 || string.IsNullOrEmpty(supplied) || supplied.Length > 1024)
            return false;
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(configured)),
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)));
    }
}

using Microsoft.AspNetCore.DataProtection;
using Samwise.Inputs.Teams;

namespace Samwise.Infrastructure.Teams.Authentication;

internal sealed class DataProtectionTeamsCredentialProtector : ITeamsCredentialProtector
{
    private readonly IDataProtector protector;

    public DataProtectionTeamsCredentialProtector(IDataProtectionProvider provider)
    {
        protector = provider.CreateProtector("Samwise.Teams.ClientSecret.v1");
    }

    public string Protect(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return protector.Protect(value);
    }

    public string Unprotect(string protectedValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedValue);
        return protector.Unprotect(protectedValue);
    }
}

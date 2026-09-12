using Microsoft.AspNetCore.DataProtection;
using Samwise.Logic.Inputs.Email;

namespace Samwise.Infrastructure.Email.Imap.Credentials;

internal sealed class DataProtectionEmailCredentialProtector : IEmailCredentialProtector
{
    private readonly IDataProtector protector;

    public DataProtectionEmailCredentialProtector(IDataProtectionProvider provider)
    {
        protector = provider.CreateProtector("Samwise.EmailAccount.Password.v1");
    }

    public string Protect(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        return protector.Protect(password);
    }

    public string Unprotect(string protectedPassword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedPassword);
        return protector.Unprotect(protectedPassword);
    }
}

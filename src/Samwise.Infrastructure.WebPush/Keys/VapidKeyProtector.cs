using Microsoft.AspNetCore.DataProtection;

namespace Samwise.Infrastructure.WebPush.Keys;

internal sealed class VapidKeyProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector protector = provider.CreateProtector("Samwise.WebPush.VapidPrivateKey.v1");

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

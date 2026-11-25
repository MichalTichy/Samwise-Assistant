using Microsoft.AspNetCore.DataProtection;
using Samwise.TaskSources.Todoist.Authentication;

namespace Samwise.Infrastructure.Todoist.Authentication;

internal sealed class DataProtectionTodoistCredentialProtector : ITodoistCredentialProtector
{
    private readonly IDataProtector protector;

    public DataProtectionTodoistCredentialProtector(IDataProtectionProvider provider)
    {
        protector = provider.CreateProtector("Samwise.Todoist.ApiToken.v1");
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

namespace Samwise.Infrastructure.Persistence.Identity;

public sealed class OwnerAccountOptions
{
    public const string SectionName = "OwnerAccount";

    public string? Email { get; init; }
}

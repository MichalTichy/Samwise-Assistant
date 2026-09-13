namespace Samwise.Infrastructure.Email.Microsoft365;

internal sealed class Microsoft365EmailOptions
{
    public const string SectionName = "Email:Microsoft365";

    public string ClientId { get; set; } = string.Empty;

    public string TenantId { get; set; } = "organizations";
}

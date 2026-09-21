namespace Samwise.Inputs.Teams.Configuration;

public sealed class TeamsGraphApplicationConfiguration
{
    public string TenantId { get; set; } = "organizations";
    public string ClientId { get; set; } = string.Empty;
    public string? ProtectedClientSecret { get; set; }

    public bool HasClientSecret => !string.IsNullOrWhiteSpace(ProtectedClientSecret);
}

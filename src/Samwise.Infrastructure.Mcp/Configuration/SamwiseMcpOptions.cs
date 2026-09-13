namespace Samwise.Infrastructure.Mcp.Configuration;

public sealed class SamwiseMcpOptions
{
    public const string SectionName = "SamwiseMcp";

    public string ApiKey { get; set; } = string.Empty;
}

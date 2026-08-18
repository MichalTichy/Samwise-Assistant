namespace Samwise.Infrastructure.AgentGateway.Agents;

public sealed class AgentRuntimeOptions
{
    public const string SectionName = "AgentRuntime";

    public string Provider { get; set; } = "Gateway";
}

public sealed class CodexGatewayOptions
{
    public const string SectionName = "CodexGateway";

    public GatewayProfileOptions Intake { get; set; } = new();
    public GatewayProfileOptions Planning { get; set; } = new();
    public GatewayProfileOptions Execution { get; set; } = new();
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(35);
}

public sealed class GatewayProfileOptions
{
    public string BaseAddress { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string ReasoningEffort { get; set; } = "high";
}

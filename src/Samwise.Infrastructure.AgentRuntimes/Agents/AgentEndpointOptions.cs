namespace Samwise.Infrastructure.AgentRuntimes.Agents;

public sealed class AgentEndpointOptions
{
    public const string SectionName = "AgentEndpoints";

    public AgentEndpointProfileOptions Intake { get; set; } = new();
    public AgentEndpointProfileOptions Planning { get; set; } = new();
    public AgentEndpointProfileOptions Execution { get; set; } = new();
    public AgentEndpointProfileOptions AutomaticApproval { get; set; } = new();
    public AgentEndpointProfileOptions AutoApprovedExecution { get; set; } = new();
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(35);
}

public sealed class AgentEndpointProfileOptions
{
    public string BaseAddress { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}

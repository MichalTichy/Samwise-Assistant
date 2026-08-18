namespace Samwise.Infrastructure.AgentRuntimes.Agents;

public sealed class AgentRuntimeOptions
{
    public const string SectionName = "AgentRuntime";

    public string Provider { get; set; } = "Gateway";
}

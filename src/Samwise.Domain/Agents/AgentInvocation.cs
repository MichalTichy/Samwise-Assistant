using Shared.Infrastructure.Persistence;

namespace Samwise.Domain.Agents;

public enum AgentInvocationStatus
{
    Running,
    Succeeded,
    Failed,
    OutcomeUnknown
}

public sealed class AgentInvocation : IItemWithId
{
    public string Id { get; set; } = string.Empty;
    public string? InboundEventId { get; set; }
    public string? WorkItemId { get; set; }
    public int WorkItemRevision { get; set; }
    public string Profile { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string ReasoningEffort { get; set; } = string.Empty;
    public string RenderedPrompt { get; set; } = string.Empty;
    public AgentInvocationToolCatalog? AvailableTools { get; set; }
    public string? CompletionId { get; set; }
    public string? RawResponse { get; set; }
    public string? Error { get; set; }
    public AgentInvocationStatus Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

public sealed class AgentInvocationToolCatalog
{
    public string CatalogVersion { get; set; } = string.Empty;
    public string RawJson { get; set; } = string.Empty;
    public List<AgentInvocationTool> Tools { get; set; } = [];
}

public sealed class AgentInvocationTool
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string InputSchemaJson { get; set; } = string.Empty;
    public bool CanInvoke { get; set; }
}

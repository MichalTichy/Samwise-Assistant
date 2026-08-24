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
    public string? TaskId { get; set; }
    public int TaskRevision { get; set; }
    public string Profile { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string RenderedPrompt { get; set; } = string.Empty;
    public string? CompletionId { get; set; }
    public string? RawResponse { get; set; }
    public string? Error { get; set; }
    public AgentInvocationStatus Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

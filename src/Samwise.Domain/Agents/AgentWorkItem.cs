namespace Samwise.Domain.Agents;

public enum AgentWorkKind
{
    ClassifyInput,
    PlanTask,
    ExecuteTask
}

public enum AgentWorkStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    OutcomeUnknown
}

public sealed class AgentWorkItem
{
    public Guid Id { get; set; }
    public AgentWorkKind Kind { get; set; }
    public Guid? InboundEventId { get; set; }
    public Guid? TaskId { get; set; }
    public int TaskRevision { get; set; }
    public Guid? PlanId { get; set; }
    public AgentWorkStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? Error { get; set; }
}

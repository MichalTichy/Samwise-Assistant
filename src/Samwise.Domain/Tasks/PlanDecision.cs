namespace Samwise.Domain.Tasks;

public enum PlanDecisionKind
{
    Approved,
    Rejected,
    Discarded
}

public sealed class PlanDecision
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public Guid? PlanId { get; set; }
    public int TaskRevision { get; set; }
    public PlanDecisionKind Decision { get; set; }
    public string? Feedback { get; set; }
    public string Source { get; set; } = "Web";
    public DateTimeOffset DecidedAt { get; set; }
}

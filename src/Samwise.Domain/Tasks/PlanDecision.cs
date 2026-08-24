using Shared.Infrastructure.Persistence;

namespace Samwise.Domain.Tasks;

public enum PlanDecisionKind
{
    Approved,
    Rejected,
    Discarded
}

public sealed class PlanDecision : IItemWithId
{
    public string Id { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string? PlanId { get; set; }
    public int TaskRevision { get; set; }
    public PlanDecisionKind Decision { get; set; }
    public string? Feedback { get; set; }
    public string Source { get; set; } = "Web";
    public DateTimeOffset DecidedAt { get; set; }
}

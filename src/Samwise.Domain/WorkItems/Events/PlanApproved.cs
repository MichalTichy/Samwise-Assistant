namespace Samwise.Domain.WorkItems.Events;

public sealed record PlanApproved(
    string WorkItemId,
    string WorkItemTitle,
    DateTimeOffset OccurredAt,
    string PlanId,
    string Source) : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

namespace Samwise.Domain.WorkItems.Events;

public sealed record PlanProposed(
    string WorkItemId,
    string WorkItemTitle,
    DateTimeOffset OccurredAt,
    string PlanId,
    int PlanNumber) : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

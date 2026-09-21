namespace Samwise.Domain.WorkItems.Events;

public sealed record ExecutionStarted(
    string WorkItemId,
    string WorkItemTitle,
    DateTimeOffset OccurredAt,
    string PlanId) : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

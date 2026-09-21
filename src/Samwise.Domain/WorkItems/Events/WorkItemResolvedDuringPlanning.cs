namespace Samwise.Domain.WorkItems.Events;

public sealed record WorkItemResolvedDuringPlanning(
    string WorkItemId,
    string WorkItemTitle,
    DateTimeOffset OccurredAt,
    string Reason) : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

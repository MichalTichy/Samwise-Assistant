namespace Samwise.Domain.WorkItems.Events;

public sealed record ExecutionCompleted(
    string WorkItemId,
    string WorkItemTitle,
    DateTimeOffset OccurredAt,
    string Summary,
    bool AutomaticallyApproved = false) : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

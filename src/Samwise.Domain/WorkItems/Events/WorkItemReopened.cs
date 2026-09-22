namespace Samwise.Domain.WorkItems.Events;

public sealed record WorkItemReopened(
    string WorkItemId,
    string WorkItemTitle,
    DateTimeOffset OccurredAt,
    string Source,
    string Reason) : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

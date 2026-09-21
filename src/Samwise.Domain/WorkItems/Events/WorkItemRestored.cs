namespace Samwise.Domain.WorkItems.Events;

public sealed record WorkItemRestored(
    string WorkItemId,
    string WorkItemTitle,
    DateTimeOffset OccurredAt,
    string Source) : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

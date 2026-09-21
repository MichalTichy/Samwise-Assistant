namespace Samwise.Domain.WorkItems.Events;

public sealed record ReplanRequested(string WorkItemId, string WorkItemTitle, DateTimeOffset OccurredAt)
    : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

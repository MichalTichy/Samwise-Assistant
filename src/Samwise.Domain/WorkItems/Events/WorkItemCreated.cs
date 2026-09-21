namespace Samwise.Domain.WorkItems.Events;

public sealed record WorkItemCreated(string WorkItemId, string WorkItemTitle, DateTimeOffset OccurredAt)
    : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

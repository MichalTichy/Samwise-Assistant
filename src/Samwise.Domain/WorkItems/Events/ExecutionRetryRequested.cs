namespace Samwise.Domain.WorkItems.Events;

public sealed record ExecutionRetryRequested(string WorkItemId, string WorkItemTitle, DateTimeOffset OccurredAt)
    : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

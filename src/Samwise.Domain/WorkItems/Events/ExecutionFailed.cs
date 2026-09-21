namespace Samwise.Domain.WorkItems.Events;

/// <summary>An approved plan failed before execution started, so no external effects happened.</summary>
public sealed record ExecutionFailed(
    string WorkItemId,
    string WorkItemTitle,
    DateTimeOffset OccurredAt,
    string? Reason) : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

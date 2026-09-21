namespace Samwise.Domain.WorkItems.Events;

/// <summary>Execution may already have caused external effects, so the outcome needs manual reconciliation.</summary>
public sealed record ExecutionOutcomeUnknown(
    string WorkItemId,
    string WorkItemTitle,
    DateTimeOffset OccurredAt,
    string? Reason) : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

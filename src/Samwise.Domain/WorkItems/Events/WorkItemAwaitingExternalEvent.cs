namespace Samwise.Domain.WorkItems.Events;

public sealed record WorkItemAwaitingExternalEvent(
    string WorkItemId,
    string WorkItemTitle,
    DateTimeOffset OccurredAt,
    string Reason,
    string AgentInvocationId) : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

namespace Samwise.Domain.WorkItems.Events;

public sealed record PlanRejected(
    string WorkItemId,
    string WorkItemTitle,
    DateTimeOffset OccurredAt,
    string PlanId,
    string Feedback,
    string Source) : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

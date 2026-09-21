namespace Samwise.Domain.WorkItems.Events;

public sealed record WorkItemContextAdded(
    string WorkItemId,
    string WorkItemTitle,
    DateTimeOffset OccurredAt,
    string Text,
    CommentAuthorKind Author) : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

using MediatR;

namespace Samwise.Domain.WorkItems.Events;

/// <summary>
/// A fact about a work item. <see cref="WorkItem"/> records one for every state transition; the caller
/// publishes them after the change is persisted, and anyone interested handles the concrete event type.
/// </summary>
public abstract record WorkItemEvent(string WorkItemId, string WorkItemTitle, DateTimeOffset OccurredAt) : INotification;

public sealed record WorkItemCreated(string WorkItemId, string WorkItemTitle, DateTimeOffset OccurredAt)
    : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

public sealed record WorkItemContextAdded(
    string WorkItemId,
    string WorkItemTitle,
    DateTimeOffset OccurredAt,
    string Text,
    CommentAuthorKind Author) : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

public sealed record PlanProposed(
    string WorkItemId,
    string WorkItemTitle,
    DateTimeOffset OccurredAt,
    string PlanId,
    int PlanNumber) : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

public sealed record PlanApproved(
    string WorkItemId,
    string WorkItemTitle,
    DateTimeOffset OccurredAt,
    string PlanId,
    string Source) : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

public sealed record PlanRejected(
    string WorkItemId,
    string WorkItemTitle,
    DateTimeOffset OccurredAt,
    string PlanId,
    string Feedback,
    string Source) : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

public sealed record PlanningQuestionAsked(
    string WorkItemId,
    string WorkItemTitle,
    DateTimeOffset OccurredAt,
    string QuestionId,
    string Prompt) : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

public sealed record PlanningQuestionAnswered(
    string WorkItemId,
    string WorkItemTitle,
    DateTimeOffset OccurredAt,
    string QuestionId,
    string Answer) : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

public sealed record PlanningFailed(
    string WorkItemId,
    string WorkItemTitle,
    DateTimeOffset OccurredAt,
    string? Reason) : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

public sealed record ReplanRequested(string WorkItemId, string WorkItemTitle, DateTimeOffset OccurredAt)
    : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

public sealed record WorkItemResolvedDuringPlanning(
    string WorkItemId,
    string WorkItemTitle,
    DateTimeOffset OccurredAt,
    string Reason) : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

public sealed record ExecutionStarted(
    string WorkItemId,
    string WorkItemTitle,
    DateTimeOffset OccurredAt,
    string PlanId) : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

public sealed record ExecutionCompleted(
    string WorkItemId,
    string WorkItemTitle,
    DateTimeOffset OccurredAt,
    string Summary) : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

/// <summary>Execution may already have caused external effects, so the outcome needs manual reconciliation.</summary>
public sealed record ExecutionOutcomeUnknown(
    string WorkItemId,
    string WorkItemTitle,
    DateTimeOffset OccurredAt,
    string? Reason) : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

/// <summary>An approved plan failed before execution started, so no external effects happened.</summary>
public sealed record ExecutionFailed(
    string WorkItemId,
    string WorkItemTitle,
    DateTimeOffset OccurredAt,
    string? Reason) : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

public sealed record ExecutionRetryRequested(string WorkItemId, string WorkItemTitle, DateTimeOffset OccurredAt)
    : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

public sealed record WorkItemDiscarded(
    string WorkItemId,
    string WorkItemTitle,
    DateTimeOffset OccurredAt,
    string Source,
    string? Reason) : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

public sealed record WorkItemRestored(
    string WorkItemId,
    string WorkItemTitle,
    DateTimeOffset OccurredAt,
    string Source) : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

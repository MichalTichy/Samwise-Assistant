namespace Samwise.Domain.WorkItems.Events;

public sealed record PlanningQuestionAsked(
    string WorkItemId,
    string WorkItemTitle,
    DateTimeOffset OccurredAt,
    string QuestionId,
    string Prompt) : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

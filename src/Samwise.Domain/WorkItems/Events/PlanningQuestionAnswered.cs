namespace Samwise.Domain.WorkItems.Events;

public sealed record PlanningQuestionAnswered(
    string WorkItemId,
    string WorkItemTitle,
    DateTimeOffset OccurredAt,
    string QuestionId,
    string Answer) : WorkItemEvent(WorkItemId, WorkItemTitle, OccurredAt);

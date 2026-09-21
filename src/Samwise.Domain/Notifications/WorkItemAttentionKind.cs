namespace Samwise.Domain.Notifications;

public enum WorkItemAttentionKind
{
    PlanAwaitingApproval = 0,
    QuestionAsked = 1,
    NeedsAttention = 2,
    OutcomeUnknown = 3,
    Completed = 4
}

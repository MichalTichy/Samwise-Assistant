namespace Samwise.Domain.WorkItems;

public enum WorkItemStatus
{
    Planning = 0,
    AwaitingApproval = 1,
    Approved = 2,
    Executing = 3,
    NeedsAttention = 4,
    Completed = 5,
    Discarded = 6,
    OutcomeUnknown = 7,
    WaitingForFollowUp = 8
}

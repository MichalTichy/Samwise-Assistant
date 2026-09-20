namespace Samwise.Domain.Inbox;

public enum InboundEventStatus
{
    Unspecified,
    Pending,
    Processed,
    NeedsReview
}

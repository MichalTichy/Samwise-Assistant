namespace Samwise.Domain.Inbox;

public enum LegacyInboundHandlingKind
{
    Pending,
    CreateWorkItem,
    AppendToWorkItem,
    Ignore,
    NeedsReview
}

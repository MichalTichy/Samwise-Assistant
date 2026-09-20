using Samwise.Domain.Inbox;

namespace Samwise.Logic.Inbox;

public sealed record InboxEventOutcomeItem(
    InboundEventOutcomeKind Kind,
    string WorkItemId,
    string RelevantText,
    string Explanation);

using Samwise.Domain.Inbox;

namespace Samwise.Logic.Inbox;

public sealed record InboxEventItem(
    string Id,
    string Source,
    string Text,
    DateTimeOffset ReceivedAt,
    InboundEventStatus Status,
    IReadOnlyList<InboxEventOutcomeItem> Outcomes,
    string Explanation);

namespace Samwise.Domain.Inbox;

public sealed class InboundEventContextItem
{
    public string SourceItemId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset? OccurredAt { get; set; }
    public bool PreviouslyProcessed { get; set; }
    public string? InboundEventId { get; set; }
    public List<string> WorkItemIds { get; set; } = [];
}

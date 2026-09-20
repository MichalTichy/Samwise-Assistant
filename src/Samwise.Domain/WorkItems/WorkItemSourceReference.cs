namespace Samwise.Domain.WorkItems;

public sealed class WorkItemSourceReference
{
    public string InboundEventId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public DateTimeOffset AddedAt { get; set; }
}

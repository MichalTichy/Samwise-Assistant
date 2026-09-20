namespace Samwise.Domain.Inbox;

public sealed class InboundEventOutcome
{
    public string Id { get; set; } = string.Empty;
    public InboundEventOutcomeKind Kind { get; set; }
    public string WorkItemId { get; set; } = string.Empty;
    public string RelevantText { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
}

using Samwise.Domain.Inbox;

namespace Samwise.Logic.Inbox.Events;

public sealed class ManualInboundEvent : InboundEvent
{
    public string Input { get; set; } = string.Empty;
    public string ManualInputId { get; set; } = string.Empty;
    public override string Source => "Manual";
    public override string ExternalId => ManualInputId;
    public override string GetIntakeText() => Input.Trim();
}

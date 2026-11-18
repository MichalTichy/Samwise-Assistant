using Samwise.Domain.Inbox;

namespace Samwise.Domain.Tasks;

public sealed class ScheduledTaskInboundEvent : InboundEvent
{
    public string TaskId { get; set; } = string.Empty;
    public string WorkItemId { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public override string Source => "Samwise task";
    public override string ExternalId => WorkItemId;
    public override string GetIntakeText() => Instructions;
}

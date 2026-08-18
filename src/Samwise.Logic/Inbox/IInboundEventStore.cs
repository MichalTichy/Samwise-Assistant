namespace Samwise.Logic.Inbox;

public sealed record InboundEventCapture(
    string Source,
    string ExternalId,
    string Text);

/// <summary>
/// Persistence boundary used by the inbound-event use cases. Every source, including
/// manual web input, enters through the same capture operation.
/// </summary>
public interface IInboundEventStore
{
    Task<Guid> CaptureAsync(
        InboundEventCapture input,
        CancellationToken cancellationToken = default);

    Task RetryAsync(Guid inboundEventId, CancellationToken cancellationToken = default);
}

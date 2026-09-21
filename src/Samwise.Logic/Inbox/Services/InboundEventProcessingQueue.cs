using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Samwise.Logic.Inbox.Services;

internal sealed class InboundEventProcessingQueue : IInboundEventProcessingQueue
{
    private readonly Channel<string> channel = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<string, byte> queued = new(StringComparer.Ordinal);

    public void Enqueue(string inboundEventId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inboundEventId);
        if (queued.TryAdd(inboundEventId, 0) && !channel.Writer.TryWrite(inboundEventId))
        {
            queued.TryRemove(inboundEventId, out _);
            throw new InvalidOperationException("The inbound event processing queue is closed.");
        }
    }

    public async ValueTask<string> DequeueAsync(CancellationToken cancellationToken)
    {
        var inboundEventId = await channel.Reader.ReadAsync(cancellationToken);
        queued.TryRemove(inboundEventId, out _);
        return inboundEventId;
    }
}

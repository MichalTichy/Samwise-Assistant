using System.Threading.Channels;

namespace Samwise.Inputs.Manual.Processing;

internal sealed class ManualInputProcessingQueue : IManualInputProcessingQueue
{
    private readonly Channel<string> channel = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(string inboundEventId)
    {
        if (!channel.Writer.TryWrite(inboundEventId))
        {
            throw new InvalidOperationException("The manual input processing queue is closed.");
        }
    }

    public ValueTask<string> DequeueAsync(CancellationToken cancellationToken) =>
        channel.Reader.ReadAsync(cancellationToken);
}

namespace Samwise.Logic.Inbox.Services;

public interface IInboundEventProcessingQueue
{
    void Enqueue(string inboundEventId);

    ValueTask<string> DequeueAsync(CancellationToken cancellationToken);
}

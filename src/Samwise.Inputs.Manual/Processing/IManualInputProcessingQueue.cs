namespace Samwise.Inputs.Manual.Processing;

internal interface IManualInputProcessingQueue
{
    void Enqueue(string inboundEventId);

    ValueTask<string> DequeueAsync(CancellationToken cancellationToken);
}

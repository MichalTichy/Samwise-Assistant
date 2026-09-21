using Samwise.Logic.Inbox.Services;

namespace Samwise.Tests.Inbox;

public sealed class InboundEventProcessingQueueTests
{
    [Fact]
    public async Task QueuePreservesDifferentInputsAndDeduplicatesQueuedIds()
    {
        var queue = new InboundEventProcessingQueue();

        queue.Enqueue("first");
        queue.Enqueue("second");
        queue.Enqueue("first");

        Assert.Equal("first", await queue.DequeueAsync(CancellationToken.None));
        Assert.Equal("second", await queue.DequeueAsync(CancellationToken.None));

        // Recovery may enqueue an ID again after it has been taken for processing.
        queue.Enqueue("first");
        Assert.Equal("first", await queue.DequeueAsync(CancellationToken.None));
    }
}

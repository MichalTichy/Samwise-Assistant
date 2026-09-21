using Samwise.Domain.Inbox;
using Samwise.Inputs.Manual.Events;
using Samwise.Inputs.Manual.Processing;
using Samwise.Inputs.Manual.Usecases;
using Samwise.Logic.Inbox.Services;

namespace Samwise.Tests.Inputs;

public sealed class ManualInputSubmissionTests
{
    [Fact]
    public async Task SubmissionQueuesPersistedEventWithoutWaitingForIntake()
    {
        var capture = new RecordingCaptureService();
        var queue = new RecordingQueue();
        var handler = new SubmitManualInputUsecaseHandler(capture, queue);

        var id = await handler.Handle(new SubmitManualInputUsecase("  Please follow up  "), CancellationToken.None);

        Assert.Equal("event-1", id);
        Assert.Equal("event-1", queue.EnqueuedId);
        var manualEvent = Assert.IsType<ManualInboundEvent>(capture.Captured);
        Assert.Equal("Please follow up", manualEvent.Input);
        Assert.Equal(InboundEventStatus.Pending, manualEvent.Status);
    }

    private sealed class RecordingCaptureService : IInboundEventCaptureService
    {
        public InboundEvent? Captured { get; private set; }

        public Task<InboundEventCaptureResult> CaptureAsync(
            InboundEvent inboundEvent,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Manual submission must not run intake inline.");

        public Task<InboundEventCaptureResult> CaptureOnlyAsync(
            InboundEvent inboundEvent,
            CancellationToken cancellationToken)
        {
            Captured = inboundEvent;
            return Task.FromResult(new InboundEventCaptureResult("event-1", false));
        }
    }

    private sealed class RecordingQueue : IManualInputProcessingQueue
    {
        public string? EnqueuedId { get; private set; }

        public void Enqueue(string inboundEventId) => EnqueuedId = inboundEventId;

        public ValueTask<string> DequeueAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}

using Samwise.Domain.Inbox;
using Samwise.Inputs.Manual.Events;
using Samwise.Inputs.Manual.Usecases;
using Samwise.Logic.Inbox.Services;

namespace Samwise.Tests.Inputs;

public sealed class ManualInputSubmissionTests
{
    [Fact]
    public async Task SubmissionQueuesPersistedEventWithoutWaitingForIntake()
    {
        var capture = new RecordingCaptureService();
        var handler = new SubmitManualInputUsecaseHandler(capture);

        var id = await handler.Handle(new SubmitManualInputUsecase("  Please follow up  "), CancellationToken.None);

        Assert.Equal("event-1", id);
        var manualEvent = Assert.IsType<ManualInboundEvent>(capture.Captured);
        Assert.Equal("Please follow up", manualEvent.Input);
        Assert.Equal(InboundEventStatus.Pending, manualEvent.Status);
    }

    private sealed class RecordingCaptureService : IInboundEventCaptureService
    {
        public InboundEvent? Captured { get; private set; }

        public Task<InboundEventCaptureResult> CaptureAsync(
            InboundEvent inboundEvent,
            CancellationToken cancellationToken)
        {
            Captured = inboundEvent;
            return Task.FromResult(new InboundEventCaptureResult("event-1", false));
        }
    }

}

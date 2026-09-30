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
        Assert.Contains("submitted by you", manualEvent.GetIntakeText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubmissionPreservesOptionalApiContextForIntake()
    {
        var capture = new RecordingCaptureService();
        var handler = new SubmitManualInputUsecaseHandler(capture);

        await handler.Handle(new SubmitManualInputUsecase(
            "  Read this article  ",
            " shared-link ",
            " Useful article ",
            " https://example.com/article ",
            " Apply it to Samwise "), CancellationToken.None);

        var manualEvent = Assert.IsType<ManualInboundEvent>(capture.Captured);
        Assert.Equal("Read this article", manualEvent.Input);
        Assert.Equal("shared-link", manualEvent.InputType);
        Assert.Equal("Useful article", manualEvent.Title);
        Assert.Equal("https://example.com/article", manualEvent.Url);
        Assert.Equal("Apply it to Samwise", manualEvent.Note);

        var intakeText = manualEvent.GetIntakeText();
        Assert.Contains("Input type: shared-link", intakeText, StringComparison.Ordinal);
        Assert.Contains("Title: Useful article", intakeText, StringComparison.Ordinal);
        Assert.Contains("URL: https://example.com/article", intakeText, StringComparison.Ordinal);
        Assert.Contains($"Text:{Environment.NewLine}Read this article", intakeText, StringComparison.Ordinal);
        Assert.Contains($"Additional context:{Environment.NewLine}Apply it to Samwise", intakeText, StringComparison.Ordinal);
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

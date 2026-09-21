using MediatR;
using Samwise.Domain.Inbox;
using Samwise.Inputs.Manual.Events;
using Samwise.Logic.Inbox.Services;
using Samwise.Inputs.Manual.Processing;

namespace Samwise.Inputs.Manual.Usecases;

public sealed record SubmitManualInputUsecase(string Text) : IRequest<string>;

internal sealed class SubmitManualInputUsecaseHandler(
    IInboundEventCaptureService captureService,
    IManualInputProcessingQueue processingQueue) : IRequestHandler<SubmitManualInputUsecase, string>
{
    public async Task<string> Handle(SubmitManualInputUsecase request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Text);

        var result = await captureService.CaptureOnlyAsync(new ManualInboundEvent
        {
            ManualInputId = Guid.NewGuid().ToString("N"),
            Input = request.Text.Trim(),
            Status = InboundEventStatus.Pending
        }, cancellationToken);

        processingQueue.Enqueue(result.InboundEventId);
        return result.InboundEventId;
    }
}

using MediatR;
using Samwise.Domain.Inbox;
using Samwise.Logic.Inbox.Services;

namespace Samwise.Logic.Inbox.Usecases;

public sealed record SubmitManualInputUsecase(string Text) : IRequest<string>;

internal sealed class SubmitManualInputUsecaseHandler(
    IInboundEventCaptureService captureService) : IRequestHandler<SubmitManualInputUsecase, string>
{
    public async Task<string> Handle(SubmitManualInputUsecase request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Text);

        var result = await captureService.CaptureAsync(new ManualInboundEvent
        {
            ManualInputId = Guid.NewGuid().ToString("N"),
            Input = request.Text.Trim(),
            Status = InboundEventStatus.Pending
        }, cancellationToken);

        return result.InboundEventId;
    }
}

using MediatR;
using Samwise.Domain.Inbox;
using Samwise.Inputs.Manual.Events;
using Samwise.Logic.Inbox.Services;

namespace Samwise.Inputs.Manual.Usecases;

public sealed record SubmitManualInputUsecase(
    string Text,
    string? InputType = null,
    string? Title = null,
    string? Url = null,
    string? Note = null) : IRequest<string>;

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
            InputType = Normalize(request.InputType),
            Title = Normalize(request.Title),
            Url = Normalize(request.Url),
            Note = Normalize(request.Note),
            Status = InboundEventStatus.Pending
        }, cancellationToken);

        return result.InboundEventId;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

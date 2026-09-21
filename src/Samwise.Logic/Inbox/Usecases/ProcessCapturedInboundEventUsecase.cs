using MediatR;
using Samwise.Logic.Inbox.Services;

namespace Samwise.Logic.Inbox.Usecases;

public sealed record ProcessCapturedInboundEventUsecase(string InboundEventId) : IRequest;

internal sealed class ProcessCapturedInboundEventUsecaseHandler(
    IInboundEventProcessingService processingService) : IRequestHandler<ProcessCapturedInboundEventUsecase>
{
    public Task Handle(ProcessCapturedInboundEventUsecase request, CancellationToken cancellationToken) =>
        processingService.ProcessAsync(request.InboundEventId, cancellationToken);
}

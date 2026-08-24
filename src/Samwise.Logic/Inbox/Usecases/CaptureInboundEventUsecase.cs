using MediatR;

namespace Samwise.Logic.Inbox.Usecases;

public sealed record CaptureInboundEventUsecase(InboundEventCapture Input) : IRequest<Guid>;

internal sealed class CaptureInboundEventUsecaseHandler(IInboundEventStore store)
    : IRequestHandler<CaptureInboundEventUsecase, Guid>
{
    public Task<Guid> Handle(CaptureInboundEventUsecase request, CancellationToken cancellationToken) =>
        store.CaptureAsync(request.Input, cancellationToken);
}

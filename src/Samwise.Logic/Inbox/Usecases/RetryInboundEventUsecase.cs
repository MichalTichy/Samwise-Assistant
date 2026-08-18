using MediatR;

namespace Samwise.Logic.Inbox.Usecases;

public sealed record RetryInboundEventUsecase(Guid InboundEventId) : IRequest;

internal sealed class RetryInboundEventUsecaseHandler(IInboundEventStore store)
    : IRequestHandler<RetryInboundEventUsecase>
{
    public Task Handle(RetryInboundEventUsecase request, CancellationToken cancellationToken) =>
        store.RetryAsync(request.InboundEventId, cancellationToken);
}

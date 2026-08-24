using MediatR;
using Samwise.Domain.Inbox;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Inbox.Usecases;

public sealed record RetryInboundEventUsecase(string InboundEventId) : IRequest;

internal sealed class RetryInboundEventUsecaseHandler(
    IRepository<InboundEvent> repository,
    ISender sender) : IRequestHandler<RetryInboundEventUsecase>
{
    public async Task Handle(RetryInboundEventUsecase request, CancellationToken cancellationToken)
    {
        var inboundEvent = await repository.GetRequiredByIdAsync(request.InboundEventId, cancellationToken);
        inboundEvent.PrepareForRetry();
        await repository.UpdateAsync(inboundEvent, cancellationToken);
        await sender.Send(new ClassifyInboundEventUsecase(inboundEvent.Id), cancellationToken);
    }
}

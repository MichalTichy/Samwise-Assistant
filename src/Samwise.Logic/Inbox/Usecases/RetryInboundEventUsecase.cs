using MediatR;
using Samwise.Domain.Inbox;
using Samwise.Logic.Inbox.Services;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Inbox.Usecases;

public sealed record RetryInboundEventUsecase(string InboundEventId) : IRequest;

internal sealed class RetryInboundEventUsecaseHandler(
    IRepository<InboundEvent> repository,
    IInboundEventProcessingService processingService) : IRequestHandler<RetryInboundEventUsecase>
{
    public async Task Handle(RetryInboundEventUsecase request, CancellationToken cancellationToken)
    {
        var inboundEvent = await repository.GetRequiredByIdAsync(request.InboundEventId, cancellationToken);
        inboundEvent.PrepareForRetry();
        await repository.UpdateAsync(inboundEvent, cancellationToken);
        await processingService.ProcessAsync(inboundEvent.Id, cancellationToken);
    }
}

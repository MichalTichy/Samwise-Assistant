using MediatR;
using Samwise.Domain.Inbox;
using Samwise.Logic.Inbox.Services;
using Samwise.Logic.Inbox.Specifications;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Inbox.Usecases;

public sealed record InboundEventCapture(string Source, string ExternalId, string Text);

public sealed record CaptureInboundEventUsecase(InboundEventCapture Input) : IRequest<string>;

internal sealed class CaptureInboundEventUsecaseHandler(
    IRepository<InboundEvent> repository,
    IInboundEventProcessingService processingService,
    TimeProvider timeProvider) : IRequestHandler<CaptureInboundEventUsecase, string>
{
    public async Task<string> Handle(CaptureInboundEventUsecase request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Input.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Input.ExternalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Input.Text);

        var source = request.Input.Source.Trim();
        var externalId = request.Input.ExternalId.Trim();
        var dedupeKey = InboundEvent.CreateDedupeKey(source, externalId);
        var existing = await repository.GetBySpecAsync(
            new InboundEventByDedupeKeySpecification(dedupeKey), cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var inboundEvent = new InboundEvent
        {
            Id = Guid.NewGuid().ToString(),
            Source = source,
            ExternalId = externalId,
            DedupeKey = dedupeKey,
            Text = request.Input.Text.Trim(),
            ReceivedAt = timeProvider.GetUtcNow(),
            Handling = InboundHandlingKind.Pending,
            Explanation = "Waiting for intake classification."
        };
        await repository.AddAsync(inboundEvent, cancellationToken);
        await processingService.ProcessAsync(inboundEvent.Id, cancellationToken);
        return inboundEvent.Id;
    }
}

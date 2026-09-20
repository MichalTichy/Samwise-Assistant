using Samwise.Domain.Inbox;
using Samwise.Logic.Inbox.Specifications;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Inbox.Services;

internal sealed record InboundEventCaptureResult(string InboundEventId, bool AlreadyExisted);

internal interface IInboundEventCaptureService
{
    Task<InboundEventCaptureResult> CaptureAsync(
        InboundEvent inboundEvent,
        CancellationToken cancellationToken);
}

internal sealed class InboundEventCaptureService(
    IRepository<InboundEvent> repository,
    IInboundEventProcessingService processingService,
    TimeProvider timeProvider) : IInboundEventCaptureService
{
    public async Task<InboundEventCaptureResult> CaptureAsync(
        InboundEvent inboundEvent,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inboundEvent.ExternalId);
        inboundEvent.NormalizeLegacyState();
        var intakeText = inboundEvent.GetIntakeText();
        if (inboundEvent.Status == InboundEventStatus.Pending)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(intakeText);
        }

        var dedupeKey = InboundEvent.CreateDedupeKey(inboundEvent.Source, inboundEvent.ExternalId);
        var existing = await repository.GetBySpecAsync(
            new InboundEventByDedupeKeySpecification(dedupeKey), cancellationToken);
        if (existing is not null)
        {
            existing.NormalizeLegacyState();
            if (existing.Status == InboundEventStatus.Pending)
            {
                await processingService.ProcessAsync(existing.Id, cancellationToken);
            }

            return new InboundEventCaptureResult(existing.Id, true);
        }

        inboundEvent.Id = string.IsNullOrWhiteSpace(inboundEvent.Id)
            ? Guid.NewGuid().ToString()
            : inboundEvent.Id;
        inboundEvent.DedupeKey = dedupeKey;
        inboundEvent.ReceivedAt = inboundEvent.ReceivedAt == default
            ? timeProvider.GetUtcNow()
            : inboundEvent.ReceivedAt;
        inboundEvent.Explanation = string.IsNullOrWhiteSpace(inboundEvent.Explanation)
            ? "Waiting for intake classification."
            : inboundEvent.Explanation.Trim();

        await repository.AddAsync(inboundEvent, cancellationToken);
        if (inboundEvent.Status == InboundEventStatus.Pending)
        {
            await processingService.ProcessAsync(inboundEvent.Id, cancellationToken);
        }

        return new InboundEventCaptureResult(inboundEvent.Id, false);
    }
}

using Samwise.Domain.Inbox;
using Samwise.Logic.Inbox.Specifications;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Inbox.Services;

public sealed record InboundEventCaptureResult(string InboundEventId, bool AlreadyExisted);

public interface IInboundEventCaptureService
{
    Task<InboundEventCaptureResult> CaptureAsync(
        InboundEvent inboundEvent,
        CancellationToken cancellationToken);
}

internal sealed class InboundEventCaptureService(
    IRepository<InboundEvent> repository,
    IInboundEventProcessingQueue processingQueue,
    TimeProvider timeProvider) : IInboundEventCaptureService
{
    public async Task<InboundEventCaptureResult> CaptureAsync(
        InboundEvent inboundEvent,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inboundEvent.ExternalId);
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
            if (existing.Status == InboundEventStatus.Pending)
            {
                processingQueue.Enqueue(existing.Id);
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
            processingQueue.Enqueue(inboundEvent.Id);
        }

        return new InboundEventCaptureResult(inboundEvent.Id, false);
    }
}

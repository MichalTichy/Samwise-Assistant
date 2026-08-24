using System.Runtime.ExceptionServices;
using Marten;
using Npgsql;
using Samwise.Logic.Inbox;
using Samwise.Logic.Time;
using Samwise.Domain.Agents;
using Samwise.Domain.Inbox;

namespace Samwise.Infrastructure.Persistence.Marten.Inbox;

internal sealed class MartenInboundEventStore(IDocumentStore store, IClock clock)
    : IInboundEventStore
{
    internal const string DedupeIndexName = "ux_inbound_event_dedupe_key";

    private const int MaximumInputLength = 20_000;
    private const int MaximumSourceLength = 100;
    private const int MaximumExternalIdLength = 512;

    public async Task<Guid> CaptureAsync(
        InboundEventCapture input,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(input);
        var dedupeKey = InboundEvent.CreateDedupeKey(normalized.Source, normalized.ExternalId);

        try
        {
            await using var session = store.LightweightSession();
            var existing = await FindExistingAsync(
                session,
                normalized.Source,
                normalized.ExternalId,
                dedupeKey,
                cancellationToken);
            if (existing is not null)
            {
                if (string.IsNullOrEmpty(existing.DedupeKey))
                {
                    existing.DedupeKey = dedupeKey;
                    session.Store(existing);
                    await session.SaveChangesAsync(cancellationToken);
                }

                return existing.Id;
            }

            var now = clock.UtcNow;
            var inboundEvent = new InboundEvent
            {
                Id = Guid.NewGuid(),
                Source = normalized.Source,
                ExternalId = normalized.ExternalId,
                DedupeKey = dedupeKey,
                Text = normalized.Text,
                ReceivedAt = now,
                Handling = InboundHandlingKind.Pending,
                Explanation = "Waiting for intake classification."
            };

            session.Store(inboundEvent);
            session.Store(CreateIntakeWorkItem(inboundEvent.Id, now));
            await session.SaveChangesAsync(cancellationToken);
            return inboundEvent.Id;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsDedupeCollision(exception))
        {
            // PostgreSQL waits for the competing unique-index transaction before raising
            // 23505, so the winning event is visible from this fresh session.
            await using var winnerSession = store.QuerySession();
            var winner = await winnerSession.Query<InboundEvent>()
                .Where(item => item.DedupeKey == dedupeKey)
                .FirstOrDefaultAsync(cancellationToken);
            if (winner is not null)
            {
                return winner.Id;
            }

            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }
    }

    public async Task RetryAsync(Guid inboundEventId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var session = store.LightweightSession();
            var inboundEvent = await session.LoadAsync<InboundEvent>(inboundEventId, cancellationToken)
                ?? throw new KeyNotFoundException($"Inbound event '{inboundEventId}' was not found.");

            var activeWorkExists = await HasActiveIntakeWorkAsync(session, inboundEventId, cancellationToken);
            if (inboundEvent.Handling == InboundHandlingKind.Pending && activeWorkExists)
            {
                return;
            }

            inboundEvent.PrepareForRetry();
            session.Store(inboundEvent);
            if (!activeWorkExists)
            {
                session.Store(CreateIntakeWorkItem(inboundEvent.Id, clock.UtcNow));
            }

            await session.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A concurrent retry may win the optimistic-concurrency race. Treat that as
            // success only when a clean session confirms both halves of the retry.
            if (await RetryIsAlreadyQueuedAsync(inboundEventId, cancellationToken))
            {
                return;
            }

            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }
    }

    private async Task<bool> RetryIsAlreadyQueuedAsync(
        Guid inboundEventId,
        CancellationToken cancellationToken)
    {
        await using var session = store.QuerySession();
        var inboundEvent = await session.LoadAsync<InboundEvent>(inboundEventId, cancellationToken);
        return inboundEvent?.Handling == InboundHandlingKind.Pending &&
               await HasActiveIntakeWorkAsync(session, inboundEventId, cancellationToken);
    }

    private static Task<bool> HasActiveIntakeWorkAsync(
        IQuerySession session,
        Guid inboundEventId,
        CancellationToken cancellationToken) =>
        session.Query<AgentWorkItem>().AnyAsync(item =>
            item.Kind == AgentWorkKind.ClassifyInput &&
            item.InboundEventId == inboundEventId &&
            (item.Status == AgentWorkStatus.Queued || item.Status == AgentWorkStatus.Running),
            cancellationToken);

    private static async Task<InboundEvent?> FindExistingAsync(
        IQuerySession session,
        string source,
        string externalId,
        string dedupeKey,
        CancellationToken cancellationToken)
    {
        var existing = await session.Query<InboundEvent>()
            .Where(item => item.DedupeKey == dedupeKey)
            .FirstOrDefaultAsync(cancellationToken);

        // This second lookup adopts any event captured before DedupeKey was introduced.
        return existing ?? await session.Query<InboundEvent>()
            .Where(item => item.Source == source && item.ExternalId == externalId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static AgentWorkItem CreateIntakeWorkItem(Guid inboundEventId, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            Kind = AgentWorkKind.ClassifyInput,
            InboundEventId = inboundEventId,
            Status = AgentWorkStatus.Queued,
            CreatedAt = now,
            AvailableAt = now
        };

    private static bool IsDedupeCollision(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres &&
                postgres.SqlState == PostgresErrorCodes.UniqueViolation &&
                string.Equals(postgres.ConstraintName, DedupeIndexName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static NormalizedCapture Normalize(InboundEventCapture input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.ExternalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Text);

        var source = input.Source.Trim();
        var externalId = input.ExternalId.Trim();
        var text = input.Text.Trim();
        if (source.Length > MaximumSourceLength)
        {
            throw new ArgumentException($"A source cannot exceed {MaximumSourceLength:N0} characters.", nameof(input));
        }

        if (externalId.Length > MaximumExternalIdLength)
        {
            throw new ArgumentException($"An external ID cannot exceed {MaximumExternalIdLength:N0} characters.", nameof(input));
        }

        if (text.Length > MaximumInputLength)
        {
            throw new ArgumentException($"An input cannot exceed {MaximumInputLength:N0} characters.", nameof(input));
        }

        return new NormalizedCapture(source, externalId, text);
    }

    private sealed record NormalizedCapture(string Source, string ExternalId, string Text);
}

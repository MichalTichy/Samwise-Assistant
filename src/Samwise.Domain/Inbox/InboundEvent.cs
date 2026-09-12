using System.Security.Cryptography;
using System.Text;
using Shared.Infrastructure.Persistence;

namespace Samwise.Domain.Inbox;

public enum InboundHandlingKind
{
    Pending,
    CreateWorkItem,
    AppendToWorkItem,
    Ignore,
    NeedsReview
}

public abstract class InboundEvent : IItemWithId
{
    public string Id { get; set; } = string.Empty;
    public string DedupeKey { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? HandledAt { get; set; }
    public InboundHandlingKind Handling { get; set; }
    public string? WorkItemId { get; set; }
    public string Explanation { get; set; } = string.Empty;

    public abstract string Source { get; }

    public abstract string ExternalId { get; }

    public abstract string GetIntakeText();

    public static string CreateDedupeKey(string source, string externalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);

        source = source.Trim();
        externalId = externalId.Trim();
        var value = $"{source.Length}:{source}:{externalId.Length}:{externalId}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    public void PrepareForRetry()
    {
        if (Handling != InboundHandlingKind.NeedsReview)
        {
            throw new InvalidOperationException("Only an inbound event needing review can be retried.");
        }

        Handling = InboundHandlingKind.Pending;
        HandledAt = null;
        WorkItemId = null;
        Explanation = "Retry requested; waiting for intake classification.";
    }
}

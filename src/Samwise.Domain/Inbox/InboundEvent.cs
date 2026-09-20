using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Shared.Infrastructure.Persistence;

namespace Samwise.Domain.Inbox;

public abstract class InboundEvent : IItemWithId
{
    public string Id { get; set; } = string.Empty;
    public string DedupeKey { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? HandledAt { get; set; }
    public InboundEventStatus Status { get; set; }
    public List<InboundEventOutcome> Outcomes { get; set; } = [];
    public string Explanation { get; set; } = string.Empty;

    // These two properties allow documents written before multi-outcome intake to be
    // normalized when they are next read. New events never populate them.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LegacyInboundHandlingKind? Handling { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkItemId { get; set; }

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
        NormalizeLegacyState();
        if (Status != InboundEventStatus.NeedsReview)
        {
            throw new InvalidOperationException("Only an inbound event needing review can be retried.");
        }

        Status = InboundEventStatus.Pending;
        HandledAt = null;
        Outcomes = [];
        Explanation = "Retry requested; waiting for intake classification.";
    }

    public void NormalizeLegacyState()
    {
        if (Status != InboundEventStatus.Unspecified)
        {
            return;
        }

        if (Handling is null)
        {
            Status = InboundEventStatus.Pending;
            return;
        }

        Status = Handling switch
        {
            LegacyInboundHandlingKind.Pending => InboundEventStatus.Pending,
            LegacyInboundHandlingKind.NeedsReview => InboundEventStatus.NeedsReview,
            _ => InboundEventStatus.Processed
        };

        if (WorkItemId is not null &&
            Handling is LegacyInboundHandlingKind.CreateWorkItem or LegacyInboundHandlingKind.AppendToWorkItem)
        {
            Outcomes =
            [
                new InboundEventOutcome
                {
                    Id = "legacy",
                    Kind = Handling == LegacyInboundHandlingKind.CreateWorkItem
                        ? InboundEventOutcomeKind.CreatedWorkItem
                        : InboundEventOutcomeKind.UpdatedWorkItem,
                    WorkItemId = WorkItemId,
                    RelevantText = GetIntakeText(),
                    Explanation = Explanation
                }
            ];
        }

        Handling = null;
        WorkItemId = null;
    }
}

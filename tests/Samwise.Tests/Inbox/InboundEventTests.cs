using Newtonsoft.Json;
using Samwise.Domain.Inbox;

namespace Samwise.Tests.Inbox;

public sealed class InboundEventTests
{
    [Fact]
    public void DedupeKeyIsStableAndNormalizesOuterWhitespace()
    {
        var first = InboundEvent.CreateDedupeKey(" Email ", " message-42 ");
        var second = InboundEvent.CreateDedupeKey("Email", "message-42");

        Assert.Equal(second, first);
        Assert.Equal(64, first.Length);
    }

    [Fact]
    public void DedupeKeyKeepsSourceAndExternalIdBoundariesUnambiguous()
    {
        var first = InboundEvent.CreateDedupeKey("a", "bc");
        var second = InboundEvent.CreateDedupeKey("ab", "c");

        Assert.NotEqual(second, first);
    }

    [Fact]
    public void PrepareForRetryReturnsNeedsReviewEventToPending()
    {
        var inboundEvent = new ManualInboundEvent
        {
            Status = InboundEventStatus.NeedsReview,
            HandledAt = DateTimeOffset.UtcNow,
            Outcomes =
            [
                new InboundEventOutcome
                {
                    WorkItemId = Guid.NewGuid().ToString()
                }
            ],
            Explanation = "Planner could not select a work item."
        };

        inboundEvent.PrepareForRetry();

        Assert.Equal(InboundEventStatus.Pending, inboundEvent.Status);
        Assert.Null(inboundEvent.HandledAt);
        Assert.Empty(inboundEvent.Outcomes);
        Assert.Equal("Retry requested; waiting for intake classification.", inboundEvent.Explanation);
    }

    [Fact]
    public void PrepareForRetryRejectsHandledEvent()
    {
        var inboundEvent = new ManualInboundEvent { Status = InboundEventStatus.Processed };

        var exception = Assert.Throws<InvalidOperationException>(inboundEvent.PrepareForRetry);

        Assert.Equal("Only an inbound event needing review can be retried.", exception.Message);
    }

    [Fact]
    public void LegacyCreatedWorkItemIsNormalizedToAnOutcome()
    {
        var inboundEvent = new ManualInboundEvent
        {
            Input = "Original input",
            Handling = LegacyInboundHandlingKind.CreateWorkItem,
            WorkItemId = "work-item-1",
            Explanation = "Created from the original event."
        };

        inboundEvent.NormalizeLegacyState();

        Assert.Equal(InboundEventStatus.Processed, inboundEvent.Status);
        var outcome = Assert.Single(inboundEvent.Outcomes);
        Assert.Equal(InboundEventOutcomeKind.CreatedWorkItem, outcome.Kind);
        Assert.Equal("work-item-1", outcome.WorkItemId);
        Assert.Equal("Original input", outcome.RelevantText);
        Assert.Null(inboundEvent.Handling);
        Assert.Null(inboundEvent.WorkItemId);
    }

    [Fact]
    public void LegacyPersistedPropertiesDeserializeBeforeNormalization()
    {
        const string json = """
            {
              "ManualInputId": "manual-1",
              "Input": "Original input",
              "Handling": "AppendToWorkItem",
              "WorkItemId": "work-item-1",
              "Explanation": "Matched an active work item."
            }
            """;

        var inboundEvent = JsonConvert.DeserializeObject<ManualInboundEvent>(json)!;

        inboundEvent.NormalizeLegacyState();

        Assert.Equal(InboundEventStatus.Processed, inboundEvent.Status);
        var outcome = Assert.Single(inboundEvent.Outcomes);
        Assert.Equal(InboundEventOutcomeKind.UpdatedWorkItem, outcome.Kind);
        Assert.Equal("work-item-1", outcome.WorkItemId);
    }
}

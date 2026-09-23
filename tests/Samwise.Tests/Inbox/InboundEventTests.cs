using Samwise.Domain.Inbox;
using Samwise.Inputs.Manual.Events;

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
        var inboundEvent = new ManualInboundEvent
        {
            Status = InboundEventStatus.Processed,
            Outcomes = [new InboundEventOutcome { WorkItemId = "work-item-1" }]
        };

        var exception = Assert.Throws<InvalidOperationException>(inboundEvent.PrepareForRetry);

        Assert.Equal("Only an inbound event needing review or an ignored event can be reprocessed.", exception.Message);
    }

    [Fact]
    public void PrepareForRetryReturnsIgnoredEventToPending()
    {
        var inboundEvent = new ManualInboundEvent
        {
            Status = InboundEventStatus.Processed,
            HandledAt = DateTimeOffset.UtcNow,
            Explanation = "No action remains."
        };

        inboundEvent.PrepareForRetry();

        Assert.Equal(InboundEventStatus.Pending, inboundEvent.Status);
        Assert.Null(inboundEvent.HandledAt);
        Assert.Empty(inboundEvent.Outcomes);
        Assert.Equal("Reprocessing requested; waiting for intake classification.", inboundEvent.Explanation);
    }

}

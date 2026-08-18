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
        var inboundEvent = new InboundEvent
        {
            Handling = InboundHandlingKind.NeedsReview,
            HandledAt = DateTimeOffset.UtcNow,
            TaskId = Guid.NewGuid(),
            Explanation = "Planner could not select a task."
        };

        inboundEvent.PrepareForRetry();

        Assert.Equal(InboundHandlingKind.Pending, inboundEvent.Handling);
        Assert.Null(inboundEvent.HandledAt);
        Assert.Null(inboundEvent.TaskId);
        Assert.Equal("Retry requested; waiting for intake classification.", inboundEvent.Explanation);
    }

    [Fact]
    public void PrepareForRetryRejectsHandledEvent()
    {
        var inboundEvent = new InboundEvent { Handling = InboundHandlingKind.Ignore };

        var exception = Assert.Throws<InvalidOperationException>(inboundEvent.PrepareForRetry);

        Assert.Equal("Only an inbound event needing review can be retried.", exception.Message);
    }
}

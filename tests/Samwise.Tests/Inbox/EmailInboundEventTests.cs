using Samwise.Domain.Inbox;

namespace Samwise.Tests.Inbox;

public sealed class EmailInboundEventTests
{
    [Fact]
    public void ExternalIdUsesStableImapIdentity()
    {
        var inboundEvent = new EmailInboundEvent
        {
            EmailAccountId = "personal",
            Mailbox = "INBOX",
            UidValidity = 42,
            Uid = 173
        };

        Assert.Equal("personal:INBOX:42:173", inboundEvent.ExternalId);
    }

    [Fact]
    public void IntakeTextIsComputedFromStoredEmailFields()
    {
        var inboundEvent = new EmailInboundEvent
        {
            FromDisplayName = "Rosie Cotton",
            FromAddress = "rosie@example.com",
            ToAddresses = ["sam@example.com"],
            Subject = "The Green Dragon",
            SentAt = new DateTimeOffset(2026, 9, 12, 8, 30, 0, TimeSpan.Zero),
            WasReadWhenCaptured = true,
            WasAnsweredWhenCaptured = true,
            BodyText = "Meet me there after supper.",
            Attachments = [new EmailAttachmentSummary("map.pdf", "application/pdf", 512)],
            Replies =
            [
                new EmailReplySnapshot(
                    "reply-1@example.com",
                    "Re: The Green Dragon",
                    "sam@example.com",
                    "Sam Gamgee",
                    ["rosie@example.com"],
                    new DateTimeOffset(2026, 9, 12, 8, 45, 0, TimeSpan.Zero),
                    "I will be there.")
            ]
        };

        var text = inboundEvent.GetIntakeText();

        Assert.Contains("Rosie Cotton <rosie@example.com>", text, StringComparison.Ordinal);
        Assert.Contains("Subject: The Green Dragon", text, StringComparison.Ordinal);
        Assert.Contains("Meet me there after supper.", text, StringComparison.Ordinal);
        Assert.Contains("map.pdf (application/pdf)", text, StringComparison.Ordinal);
        Assert.Contains("Mailbox state when captured: read, answered", text, StringComparison.Ordinal);
        Assert.Contains("Replies already sent from this account:", text, StringComparison.Ordinal);
        Assert.Contains("Sam Gamgee <sam@example.com>", text, StringComparison.Ordinal);
        Assert.Contains("I will be there.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnsweredStateExplainsWhenSentReplyWasNotAvailable()
    {
        var inboundEvent = new EmailInboundEvent
        {
            WasAnsweredWhenCaptured = true
        };

        Assert.Contains(
            "answered, sent reply could not be retrieved",
            inboundEvent.GetIntakeText(),
            StringComparison.Ordinal);
    }
}

using System.Text;

namespace Samwise.Domain.Inbox;

public sealed class EmailInboundEvent : InboundEvent
{
    public string EmailAccountId { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string AccountEmailAddress { get; set; } = string.Empty;
    public string Mailbox { get; set; } = "INBOX";
    public uint UidValidity { get; set; }
    public uint Uid { get; set; }
    public string? MessageId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromDisplayName { get; set; } = string.Empty;
    public IReadOnlyList<string> ToAddresses { get; set; } = [];
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? EmailReceivedAt { get; set; }
    public bool WasReadWhenCaptured { get; set; }
    public bool WasAnsweredWhenCaptured { get; set; }
    public string BodyText { get; set; } = string.Empty;
    public IReadOnlyList<EmailAttachmentSummary> Attachments { get; set; } = [];
    public IReadOnlyList<EmailReplySnapshot> Replies { get; set; } = [];

    public override string Source => "Email";

    public override string ExternalId => $"{EmailAccountId}:{Mailbox}:{UidValidity}:{Uid}";

    public override string GetIntakeText()
    {
        var text = new StringBuilder()
            .Append("Email received from: ")
            .AppendLine(FormatSender())
            .Append("To: ")
            .AppendLine(string.Join(", ", ToAddresses))
            .Append("Subject: ")
            .AppendLine(Subject)
            .Append("Sent: ")
            .AppendLine(SentAt?.ToString("O") ?? "Unknown")
            .Append("Mailbox state when captured: ")
            .AppendLine(FormatMailboxState())
            .AppendLine()
            .Append(BodyText.Trim());

        if (Attachments.Count > 0)
        {
            text.AppendLine()
                .AppendLine()
                .AppendLine("Attachments:");
            foreach (var attachment in Attachments)
            {
                text.Append("- ")
                    .Append(attachment.FileName)
                    .Append(" (")
                    .Append(attachment.ContentType)
                    .AppendLine(")");
            }
        }

        if (Replies.Count > 0)
        {
            text.AppendLine()
                .AppendLine()
                .AppendLine("Replies already sent from this account:");
            foreach (var reply in Replies.OrderBy(reply => reply.SentAt))
            {
                text.AppendLine()
                    .Append("Sent: ")
                    .AppendLine(reply.SentAt?.ToString("O") ?? "Unknown")
                    .Append("From: ")
                    .AppendLine(reply.FormatSender())
                    .Append("To: ")
                    .AppendLine(string.Join(", ", reply.ToAddresses))
                    .Append("Subject: ")
                    .AppendLine(reply.Subject)
                    .AppendLine()
                    .AppendLine(reply.BodyText.Trim());
            }
        }

        return text.ToString().Trim();
    }

    private string FormatMailboxState()
    {
        var states = new List<string>
        {
            WasReadWhenCaptured ? "read" : "unread",
            WasAnsweredWhenCaptured ? "answered" : "not answered"
        };
        if (WasAnsweredWhenCaptured && Replies.Count == 0)
        {
            states.Add("sent reply could not be retrieved");
        }

        return string.Join(", ", states);
    }

    private string FormatSender() => string.IsNullOrWhiteSpace(FromDisplayName)
        ? FromAddress
        : $"{FromDisplayName} <{FromAddress}>";
}

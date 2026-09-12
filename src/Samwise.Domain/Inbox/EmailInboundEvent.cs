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
    public string BodyText { get; set; } = string.Empty;
    public IReadOnlyList<EmailAttachmentSummary> Attachments { get; set; } = [];

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

        return text.ToString().Trim();
    }

    private string FormatSender() => string.IsNullOrWhiteSpace(FromDisplayName)
        ? FromAddress
        : $"{FromDisplayName} <{FromAddress}>";
}

public sealed record EmailAttachmentSummary(string FileName, string ContentType, long? Size);

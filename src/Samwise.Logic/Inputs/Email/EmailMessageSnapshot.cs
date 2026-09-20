namespace Samwise.Logic.Inputs.Email;

public sealed record EmailMessageSnapshot(
    EmailMessageDirection Direction,
    string Mailbox,
    uint UidValidity,
    uint Uid,
    string? MessageId,
    string Subject,
    string FromAddress,
    string FromDisplayName,
    IReadOnlyList<string> ToAddresses,
    DateTimeOffset? SentAt,
    DateTimeOffset? ReceivedAt,
    bool WasRead,
    bool WasAnswered,
    string BodyText,
    IReadOnlyList<EmailAttachmentSummary> Attachments,
    IReadOnlyList<EmailReplySnapshot> Replies);

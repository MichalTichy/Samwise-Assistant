using Samwise.Domain.Inbox;

namespace Samwise.Logic.Inputs.Email;

public sealed record UnreadEmailMessage(
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
    string BodyText,
    IReadOnlyList<EmailAttachmentSummary> Attachments);

public sealed record EmailAccountSaveResult(
    bool Succeeded,
    string? EmailAccountId,
    IReadOnlyList<string> Errors);

public sealed record EmailOperationResult(
    bool Succeeded,
    string Message);

public sealed record EmailProcessingResult(
    int AccountsChecked,
    int Imported,
    int Ignored,
    int AlreadyProcessed,
    IReadOnlyList<string> Errors);

public sealed record EmailConfigurationSaveResult(
    bool Succeeded,
    IReadOnlyList<string> Errors);

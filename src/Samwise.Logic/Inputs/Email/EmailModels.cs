using Samwise.Domain.Inbox;
using Samwise.Domain.Inputs.Email;

namespace Samwise.Logic.Inputs.Email;

public sealed record EmailMessageSnapshot(
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

public sealed record NewEmailReadResult(
    IReadOnlyList<EmailMessageSnapshot> Messages,
    EmailInboxCheckpoint NextCheckpoint,
    bool CheckpointWasReset);

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

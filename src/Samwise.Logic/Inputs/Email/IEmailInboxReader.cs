using Samwise.Domain.Inputs.Email;

namespace Samwise.Logic.Inputs.Email;

public interface IEmailInboxReader
{
    Task TestConnectionAsync(EmailAccount account, CancellationToken cancellationToken);

    Task<EmailInboxCheckpoint> GetCurrentInboxCheckpointAsync(
        EmailAccount account,
        CancellationToken cancellationToken);

    Task<NewEmailReadResult> ReadNewAsync(
        EmailAccount account,
        IReadOnlyCollection<string> ignoredSenderAddresses,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EmailMessageSnapshot>> ReadUnreadAsync(
        EmailAccount account,
        IReadOnlyCollection<string> ignoredSenderAddresses,
        CancellationToken cancellationToken);
}

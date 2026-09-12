using Samwise.Domain.Inputs.Email;

namespace Samwise.Logic.Inputs.Email;

public interface IEmailInboxReader
{
    Task TestConnectionAsync(EmailAccount account, CancellationToken cancellationToken);

    Task<IReadOnlyList<UnreadEmailMessage>> ReadUnreadAsync(
        EmailAccount account,
        IReadOnlyCollection<string> ignoredSenderAddresses,
        CancellationToken cancellationToken);
}

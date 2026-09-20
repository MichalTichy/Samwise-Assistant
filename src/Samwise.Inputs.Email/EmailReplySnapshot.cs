namespace Samwise.Inputs.Email;

public sealed record EmailReplySnapshot(
    string? MessageId,
    string Subject,
    string FromAddress,
    string FromDisplayName,
    IReadOnlyList<string> ToAddresses,
    DateTimeOffset? SentAt,
    string BodyText)
{
    public string FormatSender() => string.IsNullOrWhiteSpace(FromDisplayName)
        ? FromAddress
        : $"{FromDisplayName} <{FromAddress}>";
}

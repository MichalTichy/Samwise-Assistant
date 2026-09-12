using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using System.Net;
using System.Text.RegularExpressions;
using Samwise.Domain.Inbox;
using Samwise.Domain.Inputs.Email;
using Samwise.Logic.Inputs.Email;

namespace Samwise.Infrastructure.Email.Imap.Mailboxes;

internal sealed partial class MailKitEmailInboxReader(
    IEmailCredentialProtector credentialProtector) : IEmailInboxReader
{
    private const int MaximumBodyCharacters = 50_000;

    public async Task TestConnectionAsync(EmailAccount account, CancellationToken cancellationToken)
    {
        using var client = await ConnectAsync(account, cancellationToken);
        await client.Inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    public async Task<IReadOnlyList<UnreadEmailMessage>> ReadUnreadAsync(
        EmailAccount account,
        IReadOnlyCollection<string> ignoredSenderAddresses,
        CancellationToken cancellationToken)
    {
        using var client = await ConnectAsync(account, cancellationToken);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

        var uids = await inbox.SearchAsync(SearchQuery.NotSeen, cancellationToken);
        if (uids.Count == 0)
        {
            await client.DisconnectAsync(true, cancellationToken);
            return [];
        }

        var summaries = await inbox.FetchAsync(
            uids,
            MessageSummaryItems.UniqueId
            | MessageSummaryItems.Envelope
            | MessageSummaryItems.InternalDate
            | MessageSummaryItems.BodyStructure,
            cancellationToken);
        var ignored = ignoredSenderAddresses.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<UnreadEmailMessage>(summaries.Count);

        foreach (var summary in summaries.OrderBy(item => item.UniqueId.Id))
        {
            if (account.ImportAfter is { } importAfter
                && summary.InternalDate is { } internalDate
                && internalDate < importAfter)
            {
                continue;
            }

            var from = summary.Envelope?.From.Mailboxes.FirstOrDefault();
            var fromAddress = from?.Address ?? string.Empty;
            var bodyText = ignored.Contains(fromAddress)
                ? string.Empty
                : await ReadBodyTextAsync(inbox, summary, cancellationToken);
            var attachments = summary.Attachments
                .Select(part => new EmailAttachmentSummary(
                    string.IsNullOrWhiteSpace(part.FileName) ? "Unnamed attachment" : part.FileName,
                    part.ContentType.MimeType,
                    part.Octets))
                .ToList();

            result.Add(new UnreadEmailMessage(
                inbox.FullName,
                inbox.UidValidity,
                summary.UniqueId.Id,
                summary.Envelope?.MessageId,
                summary.Envelope?.Subject ?? string.Empty,
                fromAddress,
                from?.Name ?? string.Empty,
                summary.Envelope?.To.Mailboxes.Select(mailbox => mailbox.Address).ToList() ?? [],
                summary.Envelope?.Date,
                summary.InternalDate,
                bodyText,
                attachments));
        }

        await client.DisconnectAsync(true, cancellationToken);
        return result;
    }

    private async Task<ImapClient> ConnectAsync(
        EmailAccount account,
        CancellationToken cancellationToken)
    {
        var client = new ImapClient();
        try
        {
            await client.ConnectAsync(
                account.Host,
                account.Port,
                account.Security switch
                {
                    EmailConnectionSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
                    EmailConnectionSecurity.StartTls => SecureSocketOptions.StartTls,
                    _ => throw new InvalidOperationException($"Unsupported email security mode '{account.Security}'.")
                },
                cancellationToken);
            await client.AuthenticateAsync(
                account.Username,
                credentialProtector.Unprotect(account.ProtectedPassword),
                cancellationToken);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task<string> ReadBodyTextAsync(
        IMailFolder inbox,
        IMessageSummary summary,
        CancellationToken cancellationToken)
    {
        string text;
        if (summary.TextBody is not null)
        {
            var textPart = await inbox.GetBodyPartAsync(summary.UniqueId, summary.TextBody, cancellationToken)
                as TextPart;
            text = textPart?.Text ?? string.Empty;
        }
        else if (summary.HtmlBody is not null)
        {
            var htmlPart = await inbox.GetBodyPartAsync(summary.UniqueId, summary.HtmlBody, cancellationToken)
                as TextPart;
            text = ConvertHtmlToText(htmlPart?.Text ?? string.Empty);
        }
        else
        {
            text = string.Empty;
        }

        text = text.Trim();
        return text.Length <= MaximumBodyCharacters
            ? text
            : $"{text[..MaximumBodyCharacters]}\n\n[Email body truncated by Samwise.]";
    }

    private static string ConvertHtmlToText(string html)
    {
        var withoutExecutableContent = ExecutableHtmlContentRegex().Replace(html, " ");
        var withLineBreaks = HtmlBreakRegex().Replace(withoutExecutableContent, "\n");
        var withoutTags = HtmlTagRegex().Replace(withLineBreaks, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return HorizontalWhitespaceRegex().Replace(decoded, " ")
            .Replace(" \n", "\n", StringComparison.Ordinal)
            .Trim();
    }

    [GeneratedRegex("<(script|style)[^>]*>.*?</\\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ExecutableHtmlContentRegex();

    [GeneratedRegex("<(br|/p|/div|/li|/tr|/h[1-6])\\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlBreakRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("[\\t\\f\\v ]+")]
    private static partial Regex HorizontalWhitespaceRegex();
}

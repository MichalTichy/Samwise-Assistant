using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using Samwise.Domain.Inbox;
using Samwise.Domain.Inputs.Email;
using Samwise.Logic.Inputs.Email;
using System.Net;
using System.Text.RegularExpressions;

namespace Samwise.Infrastructure.Email.Imap.Mailboxes;

internal sealed partial class MailKitEmailInboxReader(
    IEmailCredentialProtector credentialProtector,
    TimeProvider timeProvider,
    ILogger<MailKitEmailInboxReader> logger) : IEmailInboxReader
{
    private const int MaximumBodyCharacters = 50_000;
    private const int MaximumReplyBodyCharacters = 20_000;
    private const int MaximumRepliesPerMessage = 5;

    public async Task TestConnectionAsync(EmailAccount account, CancellationToken cancellationToken)
    {
        using var client = await ConnectAsync(account, cancellationToken);
        await client.Inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    public async Task<EmailInboxCheckpoint> GetCurrentInboxCheckpointAsync(
        EmailAccount account,
        CancellationToken cancellationToken)
    {
        using var client = await ConnectAsync(account, cancellationToken);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
        var checkpoint = CreateCurrentCheckpoint(inbox);
        await client.DisconnectAsync(true, cancellationToken);
        return checkpoint;
    }

    public async Task<NewEmailReadResult> ReadNewAsync(
        EmailAccount account,
        IReadOnlyCollection<string> ignoredSenderAddresses,
        CancellationToken cancellationToken)
    {
        using var client = await ConnectAsync(account, cancellationToken);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
        var checkpointAtOpen = CreateCurrentCheckpoint(inbox);
        var previousCheckpoint = account.InboxCheckpoint;

        if (previousCheckpoint is null)
        {
            await client.DisconnectAsync(true, cancellationToken);
            return new NewEmailReadResult([], checkpointAtOpen, false);
        }

        if (previousCheckpoint.UidValidity != inbox.UidValidity)
        {
            await client.DisconnectAsync(true, cancellationToken);
            return new NewEmailReadResult([], checkpointAtOpen, true);
        }

        var uidRange = new UniqueIdRange(
            inbox.UidValidity,
            Math.Max(previousCheckpoint.NextUid, 1),
            uint.MaxValue);
        var uids = await inbox.SearchAsync(SearchQuery.Uids(uidRange), cancellationToken);
        var messages = await ReadMessagesAsync(
            client,
            inbox,
            uids,
            ignoredSenderAddresses,
            account.Id,
            cancellationToken);

        var nextUid = checkpointAtOpen.NextUid;
        if (messages.Count > 0)
        {
            var highestReadUid = messages.Max(message => message.Uid);
            var uidAfterHighestRead = highestReadUid == uint.MaxValue
                ? uint.MaxValue
                : highestReadUid + 1;
            nextUid = Math.Max(nextUid, uidAfterHighestRead);
        }

        nextUid = Math.Max(nextUid, previousCheckpoint.NextUid);
        var nextCheckpoint = new EmailInboxCheckpoint(
            inbox.UidValidity,
            nextUid,
            timeProvider.GetUtcNow());
        await client.DisconnectAsync(true, cancellationToken);
        return new NewEmailReadResult(messages, nextCheckpoint, false);
    }

    public async Task<IReadOnlyList<EmailMessageSnapshot>> ReadUnreadAsync(
        EmailAccount account,
        IReadOnlyCollection<string> ignoredSenderAddresses,
        CancellationToken cancellationToken)
    {
        using var client = await ConnectAsync(account, cancellationToken);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
        var uids = await inbox.SearchAsync(SearchQuery.NotSeen, cancellationToken);
        var messages = await ReadMessagesAsync(
            client,
            inbox,
            uids,
            ignoredSenderAddresses,
            account.Id,
            cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
        return messages;
    }

    private EmailInboxCheckpoint CreateCurrentCheckpoint(IMailFolder inbox) => new(
        inbox.UidValidity,
        Math.Max(inbox.UidNext?.Id ?? 1, 1),
        timeProvider.GetUtcNow());

    private async Task<IReadOnlyList<EmailMessageSnapshot>> ReadMessagesAsync(
        ImapClient client,
        IMailFolder inbox,
        IList<UniqueId> uids,
        IReadOnlyCollection<string> ignoredSenderAddresses,
        string emailAccountId,
        CancellationToken cancellationToken)
    {
        if (uids.Count == 0)
        {
            return [];
        }

        var summaries = await inbox.FetchAsync(
            uids,
            MessageSummaryItems.UniqueId
            | MessageSummaryItems.Envelope
            | MessageSummaryItems.InternalDate
            | MessageSummaryItems.Flags
            | MessageSummaryItems.BodyStructure,
            cancellationToken);
        var ignored = ignoredSenderAddresses.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var messages = new List<EmailMessageSnapshot>(summaries.Count);

        foreach (var summary in summaries.OrderBy(item => item.UniqueId.Id))
        {
            var from = summary.Envelope?.From.Mailboxes.FirstOrDefault();
            var fromAddress = from?.Address ?? string.Empty;
            var isIgnored = ignored.Contains(fromAddress);
            var bodyText = isIgnored
                ? string.Empty
                : await ReadBodyTextAsync(inbox, summary, MaximumBodyCharacters, cancellationToken);
            var attachments = summary.Attachments
                .Select(part => new EmailAttachmentSummary(
                    string.IsNullOrWhiteSpace(part.FileName) ? "Unnamed attachment" : part.FileName,
                    part.ContentType.MimeType,
                    part.Octets))
                .ToList();
            var flags = summary.Flags ?? MessageFlags.None;

            messages.Add(new EmailMessageSnapshot(
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
                flags.HasFlag(MessageFlags.Seen),
                flags.HasFlag(MessageFlags.Answered),
                bodyText,
                attachments,
                []));
        }

        return await AddSentRepliesAsync(
            client,
            messages,
            ignored,
            emailAccountId,
            cancellationToken);
    }

    private async Task<IReadOnlyList<EmailMessageSnapshot>> AddSentRepliesAsync(
        ImapClient client,
        List<EmailMessageSnapshot> messages,
        HashSet<string> ignoredSenderAddresses,
        string emailAccountId,
        CancellationToken cancellationToken)
    {
        var candidates = messages
            .Select((message, index) => new { Message = message, Index = index })
            .Where(candidate => candidate.Message.WasAnswered
                && !string.IsNullOrWhiteSpace(candidate.Message.MessageId)
                && !ignoredSenderAddresses.Contains(candidate.Message.FromAddress))
            .ToList();
        if (candidates.Count == 0)
        {
            return messages;
        }

        try
        {
            var sent = client.GetFolder(SpecialFolder.Sent);
            if (sent is null)
            {
                return messages;
            }

            await sent.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
            foreach (var candidate in candidates)
            {
                var messageId = candidate.Message.MessageId!;
                var query = SearchQuery.HeaderContains("In-Reply-To", messageId)
                    .Or(SearchQuery.HeaderContains("References", messageId));
                var matchingUids = await sent.SearchAsync(query, cancellationToken);
                if (matchingUids.Count == 0)
                {
                    continue;
                }

                var selectedUids = matchingUids
                    .OrderByDescending(uid => uid.Id)
                    .Take(MaximumRepliesPerMessage)
                    .OrderBy(uid => uid.Id)
                    .ToList();
                var summaries = await sent.FetchAsync(
                    selectedUids,
                    MessageSummaryItems.UniqueId
                    | MessageSummaryItems.Envelope
                    | MessageSummaryItems.BodyStructure,
                    cancellationToken);
                var replies = new List<EmailReplySnapshot>(summaries.Count);
                foreach (var summary in summaries.OrderBy(item => item.UniqueId.Id))
                {
                    var from = summary.Envelope?.From.Mailboxes.FirstOrDefault();
                    replies.Add(new EmailReplySnapshot(
                        summary.Envelope?.MessageId,
                        summary.Envelope?.Subject ?? string.Empty,
                        from?.Address ?? string.Empty,
                        from?.Name ?? string.Empty,
                        summary.Envelope?.To.Mailboxes.Select(mailbox => mailbox.Address).ToList() ?? [],
                        summary.Envelope?.Date,
                        await ReadBodyTextAsync(
                            sent,
                            summary,
                            MaximumReplyBodyCharacters,
                            cancellationToken)));
                }

                messages[candidate.Index] = candidate.Message with { Replies = replies };
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Sent replies could not be loaded for email account {EmailAccountId}",
                emailAccountId);
        }

        return messages;
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
        IMailFolder folder,
        IMessageSummary summary,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        string text;
        if (summary.TextBody is not null)
        {
            var textPart = await folder.GetBodyPartAsync(summary.UniqueId, summary.TextBody, cancellationToken)
                as TextPart;
            text = textPart?.Text ?? string.Empty;
        }
        else if (summary.HtmlBody is not null)
        {
            var htmlPart = await folder.GetBodyPartAsync(summary.UniqueId, summary.HtmlBody, cancellationToken)
                as TextPart;
            text = ConvertHtmlToText(htmlPart?.Text ?? string.Empty);
        }
        else
        {
            text = string.Empty;
        }

        text = text.Trim();
        return text.Length <= maximumCharacters
            ? text
            : $"{text[..maximumCharacters]}\n\n[Email body truncated by Samwise.]";
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

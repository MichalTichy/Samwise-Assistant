using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using Samwise.Domain.Inbox;
using Samwise.Domain.Inputs.Email;
using Samwise.Infrastructure.Email.Imap.Authentication;
using Samwise.Logic.Inputs.Email;
using System.Net;
using System.Text.RegularExpressions;

namespace Samwise.Infrastructure.Email.Imap.Mailboxes;

internal sealed partial class MailKitEmailInboxReader(
    IEnumerable<IEmailAuthenticationHandler> authenticationHandlers,
    TimeProvider timeProvider,
    ILogger<MailKitEmailInboxReader> logger) : IEmailInboxReader
{
    internal const string UnreadSearchQuery = "UNSEEN";

    private const int MaximumBodyCharacters = 50_000;
    private const int MaximumReplyBodyCharacters = 20_000;
    private const int MaximumRepliesPerMessage = 5;

    public async Task TestConnectionAsync(EmailAccount account, CancellationToken cancellationToken)
    {
        using var client = await ConnectAsync(account, cancellationToken);
        await client.Inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    public async Task<EmailMailboxCheckpoint> GetCurrentInboxCheckpointAsync(
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

    public async Task<EmailMailboxCheckpoint?> GetCurrentSentCheckpointAsync(
        EmailAccount account,
        CancellationToken cancellationToken)
    {
        using var client = await ConnectAsync(account, cancellationToken);
        var sent = await TryGetSentFolderAsync(client, account.Id, cancellationToken);
        if (sent is null)
        {
            await client.DisconnectAsync(true, cancellationToken);
            return null;
        }

        await sent.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
        var checkpoint = CreateCurrentCheckpoint(sent);
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

        var searchQuery = CreateNewMessagesSearchQuery(
            previousCheckpoint.NextUid,
            checkpointAtOpen.NextUid);
        if (searchQuery is null)
        {
            await client.DisconnectAsync(true, cancellationToken);
            return new NewEmailReadResult([], checkpointAtOpen, false);
        }

        var uids = await SearchWithoutExtendedResultsAsync(inbox, searchQuery, cancellationToken);
        var messages = await ReadMessagesAsync(
            client,
            inbox,
            uids,
            ignoredSenderAddresses,
            account.Id,
            cancellationToken);

        var nextCheckpoint = CreateNextCheckpoint(previousCheckpoint, checkpointAtOpen, messages);
        await client.DisconnectAsync(true, cancellationToken);
        return new NewEmailReadResult(messages, nextCheckpoint, false);
    }

    public async Task<NewEmailReadResult> ReadNewSentAsync(
        EmailAccount account,
        CancellationToken cancellationToken)
    {
        using var client = await ConnectAsync(account, cancellationToken);
        var sent = await TryGetSentFolderAsync(client, account.Id, cancellationToken);
        if (sent is null)
        {
            await client.DisconnectAsync(true, cancellationToken);
            return new NewEmailReadResult([], null, false);
        }

        await sent.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
        var checkpointAtOpen = CreateCurrentCheckpoint(sent);
        var previousCheckpoint = account.SentCheckpoint;
        if (previousCheckpoint is null)
        {
            await client.DisconnectAsync(true, cancellationToken);
            return new NewEmailReadResult([], checkpointAtOpen, false);
        }

        if (previousCheckpoint.UidValidity != sent.UidValidity)
        {
            await client.DisconnectAsync(true, cancellationToken);
            return new NewEmailReadResult([], checkpointAtOpen, true);
        }

        var searchQuery = CreateNewMessagesSearchQuery(previousCheckpoint.NextUid, checkpointAtOpen.NextUid);
        if (searchQuery is null)
        {
            await client.DisconnectAsync(true, cancellationToken);
            return new NewEmailReadResult([], checkpointAtOpen, false);
        }

        var uids = await SearchWithoutExtendedResultsAsync(sent, searchQuery, cancellationToken);
        var messages = await ReadSentMessagesAsync(sent, uids, cancellationToken);
        var nextCheckpoint = CreateNextCheckpoint(previousCheckpoint, checkpointAtOpen, messages);
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
        var uids = await SearchWithoutExtendedResultsAsync(inbox, UnreadSearchQuery, cancellationToken);
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

    private EmailMailboxCheckpoint CreateCurrentCheckpoint(IMailFolder folder) => new(
        folder.UidValidity,
        Math.Max(folder.UidNext?.Id ?? 1, 1),
        timeProvider.GetUtcNow());

    private EmailMailboxCheckpoint CreateNextCheckpoint(
        EmailMailboxCheckpoint previousCheckpoint,
        EmailMailboxCheckpoint checkpointAtOpen,
        IReadOnlyList<EmailMessageSnapshot> messages)
    {
        var nextUid = checkpointAtOpen.NextUid;
        if (messages.Count > 0)
        {
            var highestReadUid = messages.Max(message => message.Uid);
            var uidAfterHighestRead = highestReadUid == uint.MaxValue
                ? uint.MaxValue
                : highestReadUid + 1;
            nextUid = Math.Max(nextUid, uidAfterHighestRead);
        }

        return new EmailMailboxCheckpoint(
            checkpointAtOpen.UidValidity,
            Math.Max(nextUid, previousCheckpoint.NextUid),
            timeProvider.GetUtcNow());
    }

    internal static string? CreateNewMessagesSearchQuery(uint nextUid, uint inboxNextUid)
    {
        var firstUid = Math.Max(nextUid, 1);
        if (firstUid >= inboxNextUid)
        {
            return null;
        }

        return $"UID {firstUid}:{inboxNextUid - 1}";
    }

    private static async Task<IList<UniqueId>> SearchWithoutExtendedResultsAsync(
        IMailFolder folder,
        string query,
        CancellationToken cancellationToken)
    {
        // The typed MailKit overload uses ESEARCH when the server advertises it and adds
        // RETURN (ALL). Some Exchange servers reject that form with "message set is invalid".
        var imapFolder = (ImapFolder)folder;
        var results = await imapFolder.SearchAsync(query, cancellationToken);
        return results.UniqueIds;
    }

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
                EmailMessageDirection.Incoming,
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

    private static async Task<IReadOnlyList<EmailMessageSnapshot>> ReadSentMessagesAsync(
        IMailFolder sent,
        IList<UniqueId> uids,
        CancellationToken cancellationToken)
    {
        if (uids.Count == 0)
        {
            return [];
        }

        var summaries = await sent.FetchAsync(
            uids,
            MessageSummaryItems.UniqueId
            | MessageSummaryItems.Envelope
            | MessageSummaryItems.InternalDate
            | MessageSummaryItems.Flags
            | MessageSummaryItems.BodyStructure,
            cancellationToken);
        var messages = new List<EmailMessageSnapshot>(summaries.Count);
        foreach (var summary in summaries.OrderBy(item => item.UniqueId.Id))
        {
            var from = summary.Envelope?.From.Mailboxes.FirstOrDefault();
            messages.Add(new EmailMessageSnapshot(
                EmailMessageDirection.Sent,
                sent.FullName,
                sent.UidValidity,
                summary.UniqueId.Id,
                summary.Envelope?.MessageId,
                summary.Envelope?.Subject ?? string.Empty,
                from?.Address ?? string.Empty,
                from?.Name ?? string.Empty,
                summary.Envelope?.To.Mailboxes.Select(mailbox => mailbox.Address).ToList() ?? [],
                summary.Envelope?.Date,
                summary.InternalDate,
                true,
                false,
                await ReadBodyTextAsync(sent, summary, MaximumBodyCharacters, cancellationToken),
                summary.Attachments.Select(part => new EmailAttachmentSummary(
                    string.IsNullOrWhiteSpace(part.FileName) ? "Unnamed attachment" : part.FileName,
                    part.ContentType.MimeType,
                    part.Octets)).ToList(),
                []));
        }

        return messages;
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
            var sent = await TryGetSentFolderAsync(client, emailAccountId, cancellationToken);
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
            var authenticationHandler = authenticationHandlers.SingleOrDefault(
                handler => handler.Method == account.AuthenticationMethod)
                ?? throw new InvalidOperationException(
                    $"Email authentication method '{account.AuthenticationMethod}' is not installed.");
            await authenticationHandler.AuthenticateAsync(client, account, cancellationToken);

            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private async Task<IMailFolder?> TryGetSentFolderAsync(
        ImapClient client,
        string emailAccountId,
        CancellationToken cancellationToken)
    {
        try
        {
            var specialFolder = client.GetFolder(SpecialFolder.Sent);
            if (specialFolder is not null)
            {
                return specialFolder;
            }
        }
        catch (Exception exception) when (exception is FolderNotFoundException or NotSupportedException)
        {
            logger.LogDebug(
                exception,
                "Email account {EmailAccountId} does not advertise a Sent special-use folder; "
                + "falling back to folder-name discovery",
                emailAccountId);
        }

        try
        {
            var folders = new List<IMailFolder>();
            foreach (var folderNamespace in client.PersonalNamespaces)
            {
                folders.AddRange(await client.GetFoldersAsync(
                    folderNamespace,
                    subscribedOnly: false,
                    cancellationToken));
            }

            var sent = folders.FirstOrDefault(folder => folder.Attributes.HasFlag(FolderAttributes.Sent))
                ?? folders.FirstOrDefault(folder => IsSentFolderName(folder.Name));
            if (sent is not null)
            {
                logger.LogDebug(
                    "Using IMAP folder {SentFolder} as Sent mail for email account {EmailAccountId}",
                    sent.FullName,
                    emailAccountId);
            }

            return sent;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Sent folder discovery failed for email account {EmailAccountId}; "
                + "incoming mail will continue without Sent mail ingestion",
                emailAccountId);
            return null;
        }
    }

    internal static bool IsSentFolderName(string folderName)
    {
        var normalized = string.Concat(folderName
                .Normalize(System.Text.NormalizationForm.FormD)
                .Where(character => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character)
                    != System.Globalization.UnicodeCategory.NonSpacingMark))
            .Normalize(System.Text.NormalizationForm.FormC);
        normalized = new string(normalized
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

        return normalized is "sent"
            or "sentitems"
            or "sentmail"
            or "odeslane"
            or "odeslanaposta"
            or "odeslanezpravy"
            or "odeslanepolozky"
            or "gesendet"
            or "envoyes"
            or "enviados";
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

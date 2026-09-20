using System.Text.Json;
using System.Net;
using System.Text.RegularExpressions;
using Samwise.Infrastructure.Teams.Graph;
using Samwise.Inputs.Teams.Discovery;
using Samwise.Inputs.Teams.Ingestion;
using Samwise.Inputs.Teams.Ingestion.Activities;

namespace Samwise.Infrastructure.Teams.Readers;

internal sealed class MicrosoftGraphTeamsInputReader(TeamsGraphClient graph) : ITeamsInputReader
{
    public async Task<TeamsInputCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        var chats = await ReadCollectionAsync(
            graph,
            TeamsGraphCollection.Chats,
            new Dictionary<string, object?> { ["limit"] = 50 },
            cancellationToken);

        var people = new Dictionary<string, TeamsInputSourceOption>(StringComparer.Ordinal);
        var chatOptions = new List<TeamsInputSourceOption>();
        foreach (var chat in chats)
        {
            var chatId = RequiredString(chat, "id");
            var members = await ReadCollectionAsync(
                graph,
                TeamsGraphCollection.ChatMembers,
                new Dictionary<string, object?> { ["chatId"] = chatId },
                cancellationToken);
            var memberNames = new List<string>();
            foreach (var member in members)
            {
                var userId = OptionalString(member, "userId") ?? OptionalString(member, "id");
                var displayName = OptionalString(member, "displayName")
                    ?? OptionalString(member, "email")
                    ?? userId;
                if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(displayName))
                {
                    continue;
                }

                memberNames.Add(displayName);
                people.TryAdd(userId, new TeamsInputSourceOption(
                    userId,
                    displayName,
                    OptionalString(member, "email")));
            }

            var topic = OptionalString(chat, "topic");
            var chatType = OptionalString(chat, "chatType") ?? "chat";
            var label = !string.IsNullOrWhiteSpace(topic)
                ? topic
                : memberNames.Count > 0
                    ? string.Join(", ", memberNames.Take(4))
                    : chatType;
            chatOptions.Add(new TeamsInputSourceOption(chatId, label, FormatChatType(chatType)));
        }

        var teams = await ReadCollectionAsync(graph, TeamsGraphCollection.JoinedTeams, null, cancellationToken);
        var channelOptions = new List<TeamsInputSourceOption>();
        foreach (var team in teams)
        {
            var teamId = RequiredString(team, "id");
            var teamName = OptionalString(team, "displayName") ?? teamId;
            var channels = await ReadCollectionAsync(
                graph,
                TeamsGraphCollection.Channels,
                new Dictionary<string, object?> { ["teamId"] = teamId },
                cancellationToken);
            foreach (var channel in channels)
            {
                var channelId = RequiredString(channel, "id");
                var channelName = OptionalString(channel, "displayName") ?? channelId;
                channelOptions.Add(new TeamsInputSourceOption(
                    channelId,
                    $"{teamName} / {channelName}",
                    teamName));
            }
        }

        return new TeamsInputCatalog(
            people.Values.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToList(),
            chatOptions.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToList(),
            channelOptions.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToList());
    }

    public async Task<TeamsInputReadResult> ReadNewAsync(
        DateTimeOffset since,
        DateTimeOffset until,
        bool includeChatMessages,
        bool includeChannelMessages,
        bool includeCallTranscripts,
        CancellationToken cancellationToken = default)
    {
        var activities = new List<TeamsActivitySnapshot>();
        var errors = new List<string>();

        if (includeChatMessages)
        {
            try
            {
                await ReadChatMessagesAsync(graph, since, activities, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                errors.Add($"Teams chat intake failed: {exception.Message}");
            }
        }

        if (includeChannelMessages)
        {
            try
            {
                await ReadChannelMessagesAsync(graph, since, activities, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                errors.Add($"Teams channel intake failed: {exception.Message}");
            }
        }

        if (includeCallTranscripts)
        {
            try
            {
                await ReadTranscriptsAsync(graph, since, until, activities, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                errors.Add($"Teams transcript intake failed: {exception.Message}");
            }
        }

        return new TeamsInputReadResult(
            activities
                .GroupBy(activity => activity.ExternalId, StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(activity => activity.OccurredAt).First())
                .ToList(),
            errors);
    }

    private static async Task ReadChatMessagesAsync(
        TeamsGraphClient client,
        DateTimeOffset since,
        ICollection<TeamsActivitySnapshot> activities,
        CancellationToken cancellationToken)
    {
        var chats = await ReadCollectionAsync(
            client,
            TeamsGraphCollection.Chats,
            new Dictionary<string, object?> { ["limit"] = 50 },
            cancellationToken);
        foreach (var chat in chats)
        {
            var chatId = RequiredString(chat, "id");
            var chatName = OptionalString(chat, "topic") ?? FormatChatType(OptionalString(chat, "chatType") ?? "chat");
            var messages = await ReadCollectionAsync(
                client,
                TeamsGraphCollection.ChatMessages,
                new Dictionary<string, object?> { ["chatId"] = chatId, ["limit"] = 50 },
                cancellationToken);
            foreach (var message in messages)
            {
                var parsed = ParseMessage(message);
                if (parsed is not null)
                {
                    activities.Add(new TeamsChatMessageSnapshot
                    {
                        ExternalId = $"chat:{chatId}:{parsed.MessageId}:{parsed.Version}",
                        OccurredAt = parsed.OccurredAt,
                        SenderId = parsed.SenderId,
                        SenderName = parsed.SenderName,
                        ChatId = chatId,
                        ConversationName = chatName,
                        MessageId = parsed.MessageId,
                        Version = parsed.Version,
                        BodyText = parsed.BodyText,
                        ReplyToMessageId = parsed.ReplyToMessageId,
                        WebUrl = parsed.WebUrl,
                        IsNew = parsed.OccurredAt > since
                    });
                }
            }
        }
    }

    private static async Task ReadChannelMessagesAsync(
        TeamsGraphClient client,
        DateTimeOffset since,
        ICollection<TeamsActivitySnapshot> activities,
        CancellationToken cancellationToken)
    {
        var teams = await ReadCollectionAsync(client, TeamsGraphCollection.JoinedTeams, null, cancellationToken);
        foreach (var team in teams)
        {
            var teamId = RequiredString(team, "id");
            var teamName = OptionalString(team, "displayName") ?? teamId;
            var channels = await ReadCollectionAsync(
                client,
                TeamsGraphCollection.Channels,
                new Dictionary<string, object?> { ["teamId"] = teamId },
                cancellationToken);
            foreach (var channel in channels)
            {
                var channelId = RequiredString(channel, "id");
                var channelName = OptionalString(channel, "displayName") ?? channelId;
                var messages = await ReadCollectionAsync(
                    client,
                    TeamsGraphCollection.ChannelMessages,
                    new Dictionary<string, object?>
                    {
                        ["teamId"] = teamId,
                        ["channelId"] = channelId,
                        ["limit"] = 50
                    },
                    cancellationToken);
                foreach (var message in messages)
                {
                    AddChannelMessage(message, null);
                    if (message.TryGetProperty("replies", out var replies) && replies.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var reply in replies.EnumerateArray())
                        {
                            AddChannelMessage(reply, OptionalString(message, "id"));
                        }
                    }
                }

                void AddChannelMessage(JsonElement message, string? replyToId)
                {
                    var parsed = ParseMessage(message);
                    if (parsed is not null)
                    {
                        activities.Add(new TeamsChannelMessageSnapshot
                        {
                            ExternalId = $"channel:{teamId}:{channelId}:{parsed.MessageId}:{parsed.Version}",
                            OccurredAt = parsed.OccurredAt,
                            SenderId = parsed.SenderId,
                            SenderName = parsed.SenderName,
                            TeamId = teamId,
                            TeamName = teamName,
                            ChannelId = channelId,
                            ChannelName = channelName,
                            ConversationName = $"{teamName} / {channelName}",
                            MessageId = parsed.MessageId,
                            Version = parsed.Version,
                            BodyText = parsed.BodyText,
                            ReplyToMessageId = replyToId ?? parsed.ReplyToMessageId,
                            WebUrl = parsed.WebUrl,
                            IsNew = parsed.OccurredAt > since
                        });
                    }
                }
            }
        }
    }

    private static async Task ReadTranscriptsAsync(
        TeamsGraphClient client,
        DateTimeOffset since,
        DateTimeOffset until,
        ICollection<TeamsActivitySnapshot> activities,
        CancellationToken cancellationToken)
    {
        var transcripts = await ReadCollectionAsync(
            client,
            TeamsGraphCollection.RecentTranscripts,
            new Dictionary<string, object?>
            {
                ["fromDateTime"] = since.ToUniversalTime().ToString("O"),
                ["toDateTime"] = until.ToUniversalTime().ToString("O"),
                ["limit"] = 50
            },
            cancellationToken);
        foreach (var transcript in transcripts)
        {
            var createdAt = RequiredDateTime(transcript, "createdDateTime");
            var transcriptId = RequiredString(transcript, "id");
            var transcriptKind = RequiredString(transcript, "kind");
            var common = new
            {
                CreatedAt = createdAt,
                OrganizerId = OptionalString(transcript, "organizerId"),
                OrganizerName = OptionalString(transcript, "organizerName") ?? "Connected Microsoft account",
                EndedAt = OptionalDateTime(transcript, "endDateTime")
            };
            activities.Add(transcriptKind switch
            {
                "scheduledMeeting" => new TeamsScheduledMeetingTranscriptSnapshot
                {
                    ExternalId = $"transcript:{transcriptKind}:{RequiredString(transcript, "meetingId")}:{transcriptId}",
                    OccurredAt = common.CreatedAt,
                    SenderId = common.OrganizerId,
                    SenderName = common.OrganizerName,
                    EndedAt = common.EndedAt,
                    TranscriptId = transcriptId,
                    MeetingId = RequiredString(transcript, "meetingId")
                },
                "adhocCall" => new TeamsAdhocCallTranscriptSnapshot
                {
                    ExternalId = $"transcript:{transcriptKind}:{RequiredString(transcript, "callId")}:{transcriptId}",
                    OccurredAt = common.CreatedAt,
                    SenderId = common.OrganizerId,
                    SenderName = common.OrganizerName,
                    EndedAt = common.EndedAt,
                    TranscriptId = transcriptId,
                    CallId = RequiredString(transcript, "callId")
                },
                _ => throw new InvalidOperationException($"Unsupported Teams transcript kind '{transcriptKind}'.")
            });
        }
    }

    private static ParsedMessage? ParseMessage(JsonElement message)
    {
        if (OptionalString(message, "messageType") is { } messageType && messageType != "message")
        {
            return null;
        }

        if (message.TryGetProperty("deletedDateTime", out var deleted) && deleted.ValueKind != JsonValueKind.Null)
        {
            return null;
        }

        var body = message.TryGetProperty("body", out var bodyElement)
            ? OptionalString(bodyElement, "content")
            : null;
        body = NormalizeBody(body);
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var messageId = RequiredString(message, "id");
        var version = OptionalString(message, "etag")
            ?? OptionalString(message, "lastModifiedDateTime")
            ?? OptionalString(message, "createdDateTime")
            ?? messageId;
        var sender = message.TryGetProperty("from", out var from)
            && from.TryGetProperty("user", out var user)
            ? user
            : default;
        var occurredAt = OptionalDateTime(message, "lastModifiedDateTime")
            ?? RequiredDateTime(message, "createdDateTime");

        return new ParsedMessage(
            messageId,
            version,
            occurredAt,
            body,
            sender.ValueKind == JsonValueKind.Object ? OptionalString(sender, "id") : null,
            sender.ValueKind == JsonValueKind.Object ? OptionalString(sender, "displayName") : null,
            OptionalString(message, "replyToId"),
            OptionalString(message, "webUrl"));
    }

    private static async Task<IReadOnlyList<JsonElement>> ReadCollectionAsync(
        TeamsGraphClient client,
        TeamsGraphCollection collection,
        IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
    {
        string RequiredArgument(string name) => arguments?.TryGetValue(name, out var value) == true
            ? value?.ToString() ?? throw new InvalidOperationException($"Teams operation requires '{name}'.")
            : throw new InvalidOperationException($"Teams operation requires '{name}'.");
        var limit = arguments?.TryGetValue("limit", out var limitValue) == true
            ? Math.Clamp(Convert.ToInt32(limitValue), 1, 100)
            : 50;
        return collection switch
        {
            TeamsGraphCollection.Chats => await client.DelegatedCollectionAsync(
                $"me/chats?$top={limit}&$orderby=lastUpdatedDateTime desc",
                cancellationToken),
            TeamsGraphCollection.ChatMembers => await client.DelegatedCollectionAsync(
                $"chats/{TeamsGraphClient.Id(RequiredArgument("chatId"))}/members",
                cancellationToken),
            TeamsGraphCollection.ChatMessages => await client.DelegatedCollectionAsync(
                $"chats/{TeamsGraphClient.Id(RequiredArgument("chatId"))}/messages?$top={limit}",
                cancellationToken),
            TeamsGraphCollection.JoinedTeams => await client.DelegatedCollectionAsync(
                "me/joinedTeams",
                cancellationToken),
            TeamsGraphCollection.Channels => await client.DelegatedCollectionAsync(
                $"teams/{TeamsGraphClient.Id(RequiredArgument("teamId"))}/allChannels",
                cancellationToken),
            TeamsGraphCollection.ChannelMessages => await client.DelegatedCollectionAsync(
                $"teams/{TeamsGraphClient.Id(RequiredArgument("teamId"))}/channels/"
                + $"{TeamsGraphClient.Id(RequiredArgument("channelId"))}/messages?$top={limit}&$expand=replies",
                cancellationToken),
            TeamsGraphCollection.RecentTranscripts => await ReadRecentTranscriptsAsync(
                client,
                RequiredArgument("fromDateTime"),
                RequiredArgument("toDateTime"),
                limit,
                cancellationToken),
            _ => throw new InvalidOperationException($"Unknown Teams Graph collection '{collection}'.")
        };
    }

    private static async Task<IReadOnlyList<JsonElement>> ReadRecentTranscriptsAsync(
        TeamsGraphClient client,
        string fromDateTime,
        string toDateTime,
        int limit,
        CancellationToken cancellationToken)
    {
        var connectedUser = await client.DelegatedAsync("me?$select=id,displayName", cancellationToken);
        var userId = RequiredString(connectedUser, "id");
        var organizerName = OptionalString(connectedUser, "displayName") ?? "Connected Microsoft account";
        var from = Uri.EscapeDataString(DateTimeOffset.Parse(fromDateTime).ToUniversalTime().ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.Parse(toDateTime).ToUniversalTime().ToString("O"));
        var escapedUserId = TeamsGraphClient.Id(userId);
        var scheduledTask = client.ApplicationCollectionAsync(
            $"users/{escapedUserId}/onlineMeetings/getAllTranscripts("
            + $"meetingOrganizerUserId='{escapedUserId}',startDateTime={from},endDateTime={to})?$top={limit}",
            cancellationToken);
        var adhocTask = client.ApplicationCollectionAsync(
            $"users/{escapedUserId}/adhocCalls/getAllTranscripts("
            + $"userId={escapedUserId},startDateTime={from},endDateTime={to})?$top={limit}",
            cancellationToken);
        await Task.WhenAll(scheduledTask, adhocTask);

        return (await scheduledTask).Select(item => EnrichTranscript(item, "scheduledMeeting", userId, organizerName))
            .Concat((await adhocTask).Select(item => EnrichTranscript(item, "adhocCall", userId, organizerName)))
            .OrderByDescending(item => OptionalDateTime(item, "createdDateTime"))
            .Take(limit)
            .ToList();
    }

    private static JsonElement EnrichTranscript(
        JsonElement transcript,
        string kind,
        string organizerId,
        string organizerName)
    {
        var values = new Dictionary<string, object?>
        {
            ["id"] = RequiredString(transcript, "id"),
            ["kind"] = kind,
            ["meetingId"] = OptionalString(transcript, "meetingId"),
            ["callId"] = OptionalString(transcript, "callId"),
            ["organizerId"] = organizerId,
            ["organizerName"] = organizerName,
            ["createdDateTime"] = RequiredString(transcript, "createdDateTime"),
            ["endDateTime"] = OptionalString(transcript, "endDateTime")
        };
        return JsonSerializer.SerializeToElement(values);
    }

    private static string RequiredString(JsonElement item, string propertyName) =>
        OptionalString(item, propertyName)
        ?? throw new InvalidOperationException($"Teams response item has no '{propertyName}'.");

    private static string? OptionalString(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset RequiredDateTime(JsonElement item, string propertyName) =>
        OptionalDateTime(item, propertyName)
        ?? throw new InvalidOperationException($"Teams response item has no valid '{propertyName}'.");

    private static DateTimeOffset? OptionalDateTime(JsonElement item, string propertyName) =>
        OptionalString(item, propertyName) is { } value && DateTimeOffset.TryParse(value, out var parsed)
            ? parsed
            : null;

    private static string NormalizeBody(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var withLines = Regex.Replace(value, "<\\s*(br|/p|/div|/li)\\s*/?>", Environment.NewLine, RegexOptions.IgnoreCase);
        var withoutTags = Regex.Replace(withLines, "<[^>]+>", string.Empty);
        return WebUtility.HtmlDecode(withoutTags).Trim();
    }

    private static string FormatChatType(string value) => value switch
    {
        "oneOnOne" => "One-to-one chat",
        "group" => "Group chat",
        "meeting" => "Meeting chat",
        _ => value
    };

    private enum TeamsGraphCollection
    {
        Chats,
        ChatMembers,
        ChatMessages,
        JoinedTeams,
        Channels,
        ChannelMessages,
        RecentTranscripts
    }

    private sealed record ParsedMessage(
        string MessageId,
        string Version,
        DateTimeOffset OccurredAt,
        string BodyText,
        string? SenderId,
        string? SenderName,
        string? ReplyToMessageId,
        string? WebUrl);
}

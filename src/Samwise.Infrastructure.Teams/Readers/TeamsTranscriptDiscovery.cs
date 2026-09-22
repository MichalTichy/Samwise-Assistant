using System.Text.Json;
using Samwise.Infrastructure.Teams.Graph;

namespace Samwise.Infrastructure.Teams.Readers;

internal static class TeamsTranscriptDiscovery
{
    private const int ResultLimit = 50;
    private const int CalendarLookbackDays = 7;
    private const int TranscriptReplayDays = 1;

    public static async Task<IReadOnlyList<JsonElement>> ReadOrganizedAsync(
        ITeamsGraphClient client,
        DateTimeOffset since,
        DateTimeOffset until,
        CancellationToken cancellationToken)
    {
        var connectedUser = await client.GetDelegatedAsync("me?$select=id,displayName", cancellationToken);
        var userId = RequiredString(connectedUser, "id");
        var organizerName = OptionalString(connectedUser, "displayName") ?? "Connected Microsoft account";
        var from = Uri.EscapeDataString(since.ToUniversalTime().ToString("O"));
        var to = Uri.EscapeDataString(until.ToUniversalTime().ToString("O"));
        var escapedUserId = TeamsGraphClient.Id(userId);

        var organizedTask = client.GetApplicationCollectionAsync(
            $"users/{escapedUserId}/onlineMeetings/getAllTranscripts("
            + $"meetingOrganizerUserId='{escapedUserId}',startDateTime={from},endDateTime={to})?$top={ResultLimit}",
            cancellationToken);
        var adhocTask = client.GetApplicationCollectionAsync(
            $"users/{escapedUserId}/adhocCalls/getAllTranscripts("
            + $"userId={escapedUserId},startDateTime={from},endDateTime={to})?$top={ResultLimit}",
            cancellationToken);
        await Task.WhenAll(organizedTask, adhocTask);

        return (await organizedTask)
            .Select(item => EnrichTranscript(item, "scheduledMeeting", userId, organizerName))
            .Concat((await adhocTask)
                .Select(item => EnrichTranscript(item, "adhocCall", userId, organizerName)))
            .Where(item => IsInWindow(item, since, until))
            .Take(ResultLimit)
            .ToList();
    }

    public static async Task<IReadOnlyList<JsonElement>> ReadInvitedAsync(
        ITeamsGraphClient client,
        DateTimeOffset since,
        DateTimeOffset until,
        CancellationToken cancellationToken)
    {
        var meetings = await client.GetDelegatedCollectionAsync(
            CalendarViewPath(since.AddDays(-CalendarLookbackDays), until),
            cancellationToken);
        var transcripts = new List<JsonElement>();
        var seenJoinUrls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var calendarEvent in meetings)
        {
            if (!calendarEvent.TryGetProperty("onlineMeeting", out var onlineMeeting)
                || onlineMeeting.ValueKind != JsonValueKind.Object
                || OptionalString(onlineMeeting, "joinUrl") is not { } joinUrl
                || !seenJoinUrls.Add(joinUrl))
            {
                continue;
            }

            var resolvedMeetings = await client.GetDelegatedCollectionAsync(
                MeetingResolutionPath(joinUrl), cancellationToken);
            foreach (var meeting in resolvedMeetings)
            {
                var meetingId = RequiredString(meeting, "id");
                var organizer = meeting.TryGetProperty("participants", out var participants)
                    && participants.TryGetProperty("organizer", out var value)
                    ? value : default;
                var identity = organizer.ValueKind == JsonValueKind.Object
                    && organizer.TryGetProperty("identity", out var organizerIdentity)
                    && organizerIdentity.TryGetProperty("user", out var user)
                    ? user : default;
                var organizerId = identity.ValueKind == JsonValueKind.Object
                    ? OptionalString(identity, "id") : null;
                var eventOrganizer = calendarEvent.TryGetProperty("organizer", out var eventOrganizerValue)
                    && eventOrganizerValue.TryGetProperty("emailAddress", out var emailAddress)
                    ? emailAddress : default;
                var organizerName = eventOrganizer.ValueKind == JsonValueKind.Object
                    ? OptionalString(eventOrganizer, "name") : null;

                var meetingTranscripts = await client.GetDelegatedCollectionAsync(
                    $"me/onlineMeetings/{TeamsGraphClient.Id(meetingId)}/transcripts?$top={ResultLimit}",
                    cancellationToken);
                transcripts.AddRange(meetingTranscripts.Select(item => EnrichTranscript(
                    item, "scheduledMeeting", organizerId, organizerName ?? "Meeting organizer", meetingId)));
            }
        }

        // Revisit recent participant transcripts that the organizer-only poll missed before this feature.
        // Inbound-event external IDs make repeated discovery safe.
        return transcripts
            .Where(item => IsInWindow(item, since.AddDays(-TranscriptReplayDays), until))
            .OrderByDescending(item => DateTimeOffset.Parse(RequiredString(item, "createdDateTime")))
            .Take(ResultLimit)
            .ToList();
    }

    internal static string CalendarViewPath(DateTimeOffset start, DateTimeOffset end) =>
        "me/calendarView?startDateTime=" + Uri.EscapeDataString(start.ToUniversalTime().ToString("O"))
        + "&endDateTime=" + Uri.EscapeDataString(end.ToUniversalTime().ToString("O"))
        + "&$select=onlineMeeting,organizer&$top=1000";

    internal static string MeetingResolutionPath(string joinUrl) =>
        "me/onlineMeetings?$filter=" + Uri.EscapeDataString(
            $"JoinWebUrl eq '{joinUrl.Replace("'", "''", StringComparison.Ordinal)}'");

    private static JsonElement EnrichTranscript(
        JsonElement transcript,
        string kind,
        string? organizerId,
        string organizerName,
        string? resolvedMeetingId = null)
    {
        var values = new Dictionary<string, object?>
        {
            ["id"] = RequiredString(transcript, "id"),
            ["kind"] = kind,
            ["meetingId"] = resolvedMeetingId ?? OptionalString(transcript, "meetingId"),
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

    private static bool IsInWindow(JsonElement transcript, DateTimeOffset since, DateTimeOffset until) =>
        DateTimeOffset.TryParse(OptionalString(transcript, "createdDateTime"), out var createdAt)
        && createdAt > since && createdAt <= until;
}

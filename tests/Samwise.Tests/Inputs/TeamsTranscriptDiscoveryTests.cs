using System.Text.Json;
using Samwise.Infrastructure.Teams.Graph;
using Samwise.Infrastructure.Teams.Readers;

namespace Samwise.Tests.Inputs;

public sealed class TeamsTranscriptDiscoveryTests
{
    private static readonly DateTimeOffset Since = new(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Until = Since.AddHours(2);

    [Fact]
    public async Task DiscoversTranscriptOfMeetingOrganizedByAnotherParticipant()
    {
        var graph = new RecordingGraphClient();

        var result = await TeamsTranscriptDiscovery.ReadInvitedAsync(graph, Since, Until, CancellationToken.None);

        var transcript = Assert.Single(result);
        Assert.Equal("transcript-1", transcript.GetProperty("id").GetString());
        Assert.Equal("meeting-1", transcript.GetProperty("meetingId").GetString());
        Assert.Equal("colleague-1", transcript.GetProperty("organizerId").GetString());
        Assert.Equal("Colleague", transcript.GetProperty("organizerName").GetString());
        Assert.Contains(graph.DelegatedPaths, path => path.StartsWith("me/calendarView?", StringComparison.Ordinal));
        Assert.Contains(graph.DelegatedPaths, path => path.StartsWith("me/onlineMeetings?$filter=", StringComparison.Ordinal));
        Assert.Contains("me/onlineMeetings/meeting-1/transcripts?$top=50", graph.DelegatedPaths);
    }

    [Fact]
    public async Task KeepsExistingOrganizerFeed()
    {
        var graph = new RecordingGraphClient(includeOrganizedTranscript: true);

        var result = await TeamsTranscriptDiscovery.ReadOrganizedAsync(graph, Since, Until, CancellationToken.None);

        Assert.Equal("meeting-1", Assert.Single(result).GetProperty("meetingId").GetString());
    }

    [Fact]
    public async Task ReplaysRecentParticipantTranscriptMissedBeforeUpgrade()
    {
        var graph = new RecordingGraphClient(createdAt: Since.AddMinutes(-1));

        var result = await TeamsTranscriptDiscovery.ReadInvitedAsync(graph, Since, Until, CancellationToken.None);

        Assert.Single(result);
    }

    [Fact]
    public async Task IgnoresOldTranscriptsFromCalendarLookback()
    {
        var graph = new RecordingGraphClient(createdAt: Since.AddDays(-2));

        var result = await TeamsTranscriptDiscovery.ReadInvitedAsync(graph, Since, Until, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public void MeetingResolutionEscapesODataStringAndUrl()
    {
        var path = TeamsTranscriptDiscovery.MeetingResolutionPath("https://teams.example/meet?name=O'Brien");

        Assert.Equal(
            "JoinWebUrl eq 'https://teams.example/meet?name=O''Brien'",
            Uri.UnescapeDataString(path["me/onlineMeetings?$filter=".Length..]));
    }

    private sealed class RecordingGraphClient(
        DateTimeOffset? createdAt = null,
        bool includeOrganizedTranscript = false) : ITeamsGraphClient
    {
        public List<string> DelegatedPaths { get; } = [];

        public Task<JsonElement> GetDelegatedAsync(string path, CancellationToken cancellationToken)
        {
            DelegatedPaths.Add(path);
            return Task.FromResult(Parse("""{"id":"connected-1","displayName":"Connected user"}"""));
        }

        public Task<IReadOnlyList<JsonElement>> GetDelegatedCollectionAsync(
            string path,
            CancellationToken cancellationToken)
        {
            DelegatedPaths.Add(path);
            var items = path switch
            {
                _ when path.StartsWith("me/calendarView?", StringComparison.Ordinal) =>
                    """[{"onlineMeeting":{"joinUrl":"https://teams.example/meeting"},"organizer":{"emailAddress":{"name":"Colleague"}}}]""",
                _ when path.StartsWith("me/onlineMeetings?$filter=", StringComparison.Ordinal) =>
                    """[{"id":"meeting-1","participants":{"organizer":{"identity":{"user":{"id":"colleague-1"}}}}}]""",
                _ when path.StartsWith("me/onlineMeetings/meeting-1/transcripts?", StringComparison.Ordinal) =>
                    $"[{{\"id\":\"transcript-1\",\"createdDateTime\":\"{(createdAt ?? Since.AddMinutes(30)):O}\"}}]",
                _ => throw new InvalidOperationException($"Unexpected Graph path: {path}")
            };
            return Task.FromResult<IReadOnlyList<JsonElement>>(Parse(items).EnumerateArray().Select(item => item.Clone()).ToList());
        }

        public Task<IReadOnlyList<JsonElement>> GetApplicationCollectionAsync(
            string path,
            CancellationToken cancellationToken)
        {
            var items = includeOrganizedTranscript && path.Contains("onlineMeetings", StringComparison.Ordinal)
                ? $"[{{\"id\":\"transcript-1\",\"meetingId\":\"meeting-1\",\"createdDateTime\":\"{(createdAt ?? Since.AddMinutes(30)):O}\"}}]"
                : "[]";
            return Task.FromResult<IReadOnlyList<JsonElement>>(Parse(items).EnumerateArray().Select(item => item.Clone()).ToList());
        }

        private static JsonElement Parse(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
    }
}

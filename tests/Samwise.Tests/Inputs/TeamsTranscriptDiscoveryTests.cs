using System.Text.Json;
using System.Net;
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

        var transcript = Assert.Single(result.Transcripts);
        Assert.Empty(result.Errors);
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

        Assert.Equal("meeting-1", Assert.Single(result.Transcripts).GetProperty("meetingId").GetString());
        Assert.Empty(result.Errors);
        Assert.Contains(graph.ApplicationPaths, path =>
            path.Contains("userId='connected-1'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AdhocFailureDoesNotHideScheduledMeetingTranscript()
    {
        var graph = new RecordingGraphClient(
            includeOrganizedTranscript: true,
            failAdhocCalls: true);

        var result = await TeamsTranscriptDiscovery.ReadOrganizedAsync(
            graph, Since, Until, CancellationToken.None);

        Assert.Single(result.Transcripts);
        Assert.Contains("Ad-hoc calls", Assert.Single(result.Errors));
    }

    [Fact]
    public async Task ReplaysRecentParticipantTranscriptMissedBeforeUpgrade()
    {
        var graph = new RecordingGraphClient(createdAt: Since.AddMinutes(-1));

        var result = await TeamsTranscriptDiscovery.ReadInvitedAsync(graph, Since, Until, CancellationToken.None);

        Assert.Single(result.Transcripts);
    }

    [Fact]
    public async Task IgnoresOldTranscriptsFromCalendarLookback()
    {
        var graph = new RecordingGraphClient(createdAt: Since.AddDays(-2));

        var result = await TeamsTranscriptDiscovery.ReadInvitedAsync(graph, Since, Until, CancellationToken.None);

        Assert.Empty(result.Transcripts);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "3003: User does not have access to lookup meeting")]
    [InlineData(HttpStatusCode.NotFound, "3004: Meeting properties are not found")]
    public async Task InaccessibleMeetingIsSkippedWithoutBlockingAnotherTranscript(
        HttpStatusCode statusCode,
        string message)
    {
        var graph = new RecordingGraphClient(
            includeInaccessibleMeeting: true,
            inaccessibleStatusCode: statusCode);

        var result = await TeamsTranscriptDiscovery.ReadInvitedAsync(graph, Since, Until, CancellationToken.None);

        Assert.Single(result.Transcripts);
        Assert.Empty(result.Errors);
        Assert.Contains(message, Assert.Single(result.SkippedMeetings));
    }

    [Fact]
    public async Task TenantWideTranscriptFailureRemainsRetryable()
    {
        var graph = new RecordingGraphClient(includeInaccessibleMeeting: true, tenantAccessDisabled: true);

        var result = await TeamsTranscriptDiscovery.ReadInvitedAsync(graph, Since, Until, CancellationToken.None);

        Assert.Single(result.Transcripts);
        Assert.Contains("disabled for this tenant", Assert.Single(result.Errors));
        Assert.Empty(result.SkippedMeetings);
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
        bool includeOrganizedTranscript = false,
        bool includeInaccessibleMeeting = false,
        bool tenantAccessDisabled = false,
        HttpStatusCode inaccessibleStatusCode = HttpStatusCode.Forbidden,
        bool failAdhocCalls = false) : ITeamsGraphClient
    {
        public List<string> DelegatedPaths { get; } = [];
        public List<string> ApplicationPaths { get; } = [];

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
            if (path.Contains("inaccessible", StringComparison.Ordinal))
            {
                throw tenantAccessDisabled
                    ? new TeamsGraphRequestException(
                        HttpStatusCode.Forbidden, "Graph API access to transcripts is disabled for this tenant.")
                    : new TeamsGraphRequestException(
                        inaccessibleStatusCode,
                        inaccessibleStatusCode == HttpStatusCode.NotFound
                            ? "3004: Meeting properties are not found"
                            : "3003: User does not have access to lookup meeting");
            }

            var items = path switch
            {
                _ when path.StartsWith("me/calendarView?", StringComparison.Ordinal) =>
                    includeInaccessibleMeeting
                        ? """[{"subject":"Accessible","onlineMeeting":{"joinUrl":"https://teams.example/meeting"},"organizer":{"emailAddress":{"name":"Colleague"}}},{"subject":"Inaccessible","onlineMeeting":{"joinUrl":"https://teams.example/inaccessible"}}]"""
                        : """[{"onlineMeeting":{"joinUrl":"https://teams.example/meeting"},"organizer":{"emailAddress":{"name":"Colleague"}}}]""",
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
            ApplicationPaths.Add(path);
            if (failAdhocCalls && path.Contains("adhocCalls", StringComparison.Ordinal))
            {
                throw new TeamsGraphRequestException(HttpStatusCode.BadRequest,
                    "Expression of type 'Edm.Guid' cannot be converted to type 'Edm.String'.");
            }

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

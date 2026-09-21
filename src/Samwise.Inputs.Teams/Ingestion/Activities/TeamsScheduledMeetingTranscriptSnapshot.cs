namespace Samwise.Inputs.Teams.Ingestion.Activities;

public sealed record TeamsScheduledMeetingTranscriptSnapshot : TeamsTranscriptSnapshot
{
    public required string MeetingId { get; init; }
}

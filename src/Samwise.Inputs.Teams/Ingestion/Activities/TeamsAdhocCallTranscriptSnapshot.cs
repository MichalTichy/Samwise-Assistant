namespace Samwise.Inputs.Teams.Ingestion.Activities;

public sealed record TeamsAdhocCallTranscriptSnapshot : TeamsTranscriptSnapshot
{
    public required string CallId { get; init; }
}

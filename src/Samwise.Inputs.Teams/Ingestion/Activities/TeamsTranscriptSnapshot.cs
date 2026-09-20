namespace Samwise.Inputs.Teams.Ingestion.Activities;

public abstract record TeamsTranscriptSnapshot : TeamsActivitySnapshot
{
    public required string TranscriptId { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
}

namespace Samwise.Inputs.Teams.Ingestion.Activities;

public abstract record TeamsActivitySnapshot
{
    public required string ExternalId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public string? SenderId { get; init; }
    public string? SenderName { get; init; }
}

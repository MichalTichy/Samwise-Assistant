namespace Samwise.Inputs.Teams.Ingestion.Activities;

public abstract record TeamsMessageSnapshot : TeamsActivitySnapshot
{
    public required string ConversationName { get; init; }
    public required string MessageId { get; init; }
    public required string Version { get; init; }
    public required string BodyText { get; init; }
    public string? ReplyToMessageId { get; init; }
    public string? WebUrl { get; init; }
    public bool IsNew { get; init; } = true;
}

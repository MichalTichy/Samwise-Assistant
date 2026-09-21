namespace Samwise.Inputs.Teams.Ingestion.Activities;

public sealed record TeamsChatMessageSnapshot : TeamsMessageSnapshot
{
    public required string ChatId { get; init; }
}

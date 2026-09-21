namespace Samwise.Inputs.Teams.Ingestion.Activities;

public sealed record TeamsChannelMessageSnapshot : TeamsMessageSnapshot
{
    public required string TeamId { get; init; }
    public required string TeamName { get; init; }
    public required string ChannelId { get; init; }
    public required string ChannelName { get; init; }
}

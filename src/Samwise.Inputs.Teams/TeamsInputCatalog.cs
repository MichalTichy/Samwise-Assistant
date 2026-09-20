namespace Samwise.Inputs.Teams;

public sealed record TeamsInputCatalog(
    IReadOnlyList<TeamsInputSourceOption> People,
    IReadOnlyList<TeamsInputSourceOption> Chats,
    IReadOnlyList<TeamsInputSourceOption> Channels);

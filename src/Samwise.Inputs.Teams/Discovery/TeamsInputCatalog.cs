namespace Samwise.Inputs.Teams.Discovery;

public sealed record TeamsInputCatalog(
    IReadOnlyList<TeamsInputSourceOption> People,
    IReadOnlyList<TeamsInputSourceOption> Chats,
    IReadOnlyList<TeamsInputSourceOption> Channels);

namespace Samwise.Inputs.Teams;

public sealed record TeamsConnectionState(
    bool Connected,
    string? Username,
    TeamsAuthorizationChallenge? PendingChallenge,
    string? Error);

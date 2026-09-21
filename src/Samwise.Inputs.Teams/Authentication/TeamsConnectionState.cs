namespace Samwise.Inputs.Teams.Authentication;

public sealed record TeamsConnectionState(
    bool Connected,
    string? Username,
    TeamsAuthorizationChallenge? PendingChallenge,
    string? Error);

namespace Samwise.Inputs.Teams.Authentication;

public sealed record TeamsAuthorizationOperationResult(
    bool Succeeded,
    TeamsAuthorizationChallenge? Challenge,
    IReadOnlyList<string> Errors);

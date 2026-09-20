namespace Samwise.Inputs.Teams;

public sealed record TeamsAuthorizationOperationResult(
    bool Succeeded,
    TeamsAuthorizationChallenge? Challenge,
    IReadOnlyList<string> Errors);

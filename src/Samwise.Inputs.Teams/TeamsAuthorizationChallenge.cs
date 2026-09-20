namespace Samwise.Inputs.Teams;

public sealed record TeamsAuthorizationChallenge(
    string UserCode,
    string VerificationUrl,
    string Message,
    DateTimeOffset ExpiresAt);

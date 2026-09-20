namespace Samwise.Inputs.Teams.Authentication;

public sealed record TeamsAuthorizationChallenge(
    string UserCode,
    string VerificationUrl,
    string Message,
    DateTimeOffset ExpiresAt);

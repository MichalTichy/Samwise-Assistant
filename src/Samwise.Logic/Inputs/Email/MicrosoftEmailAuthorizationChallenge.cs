namespace Samwise.Logic.Inputs.Email;

public sealed record MicrosoftEmailAuthorizationChallenge(
    string AuthorizationId,
    string UserCode,
    string VerificationUrl,
    string Message,
    DateTimeOffset ExpiresAt);

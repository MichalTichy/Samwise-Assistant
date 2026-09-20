namespace Samwise.Inputs.Email;

public sealed record MicrosoftEmailAuthorizationStartResult(
    bool Succeeded,
    MicrosoftEmailAuthorizationChallenge? Challenge,
    IReadOnlyList<string> Errors);

namespace Samwise.Logic.Inputs.Email;

public sealed record MicrosoftEmailAuthorizationStartResult(
    bool Succeeded,
    MicrosoftEmailAuthorizationChallenge? Challenge,
    IReadOnlyList<string> Errors);

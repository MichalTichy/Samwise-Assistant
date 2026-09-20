namespace Samwise.Inputs.Email;

public sealed record MicrosoftEmailAuthorizationCompletion(
    MicrosoftEmailAuthorizationState State,
    string? TokenCache,
    string? HomeAccountId,
    string? Username,
    string? Error);

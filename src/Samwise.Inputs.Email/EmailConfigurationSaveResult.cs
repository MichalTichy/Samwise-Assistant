namespace Samwise.Inputs.Email;

public sealed record EmailConfigurationSaveResult(
    bool Succeeded,
    IReadOnlyList<string> Errors);

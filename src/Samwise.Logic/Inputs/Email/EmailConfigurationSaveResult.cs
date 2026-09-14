namespace Samwise.Logic.Inputs.Email;

public sealed record EmailConfigurationSaveResult(
    bool Succeeded,
    IReadOnlyList<string> Errors);

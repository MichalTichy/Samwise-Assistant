namespace Samwise.Logic.Inputs.Email;

public sealed record EmailAccountSaveResult(
    bool Succeeded,
    string? EmailAccountId,
    IReadOnlyList<string> Errors);

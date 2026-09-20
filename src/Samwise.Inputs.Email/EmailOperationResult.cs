namespace Samwise.Inputs.Email;

public sealed record EmailOperationResult(
    bool Succeeded,
    string Message);

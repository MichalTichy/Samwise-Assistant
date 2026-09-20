namespace Samwise.Inputs.Email;

public sealed record EmailProcessingResult(
    int AccountsChecked,
    int Imported,
    int Ignored,
    int AlreadyProcessed,
    IReadOnlyList<string> Errors);

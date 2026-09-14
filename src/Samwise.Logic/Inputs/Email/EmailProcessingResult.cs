namespace Samwise.Logic.Inputs.Email;

public sealed record EmailProcessingResult(
    int AccountsChecked,
    int Imported,
    int Ignored,
    int AlreadyProcessed,
    IReadOnlyList<string> Errors);

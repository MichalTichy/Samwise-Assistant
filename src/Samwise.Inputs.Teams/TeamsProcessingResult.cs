namespace Samwise.Inputs.Teams;

public sealed record TeamsProcessingResult(
    int Imported,
    int Ignored,
    int AlreadyProcessed,
    IReadOnlyList<string> Errors);

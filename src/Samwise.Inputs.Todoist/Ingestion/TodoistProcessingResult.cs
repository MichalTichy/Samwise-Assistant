namespace Samwise.Inputs.Todoist.Ingestion;

public sealed record TodoistProcessingResult(
    int Imported,
    int Ignored,
    int AlreadyProcessed,
    IReadOnlyList<string> Errors);

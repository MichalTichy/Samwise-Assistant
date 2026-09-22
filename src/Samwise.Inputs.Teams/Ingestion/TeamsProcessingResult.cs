namespace Samwise.Inputs.Teams.Ingestion;

public sealed record TeamsProcessingResult(
    int Imported,
    int Ignored,
    int AlreadyProcessed,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> SkippedMeetings);

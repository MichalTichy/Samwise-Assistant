namespace Samwise.TaskSources.Todoist.Synchronization;

public sealed record TodoistSynchronizationResult(
    int Imported,
    int Ignored,
    int AlreadyProcessed,
    IReadOnlyList<string> Errors);

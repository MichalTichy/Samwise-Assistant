namespace Samwise.TaskSources.Todoist.Discovery;

public sealed record TodoistSourceCatalog(
    string? AccountName,
    IReadOnlyList<TodoistSourceOption> Projects,
    IReadOnlyList<TodoistSourceOption> Labels);

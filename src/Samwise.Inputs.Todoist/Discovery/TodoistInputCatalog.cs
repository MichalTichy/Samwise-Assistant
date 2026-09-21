namespace Samwise.Inputs.Todoist.Discovery;

public sealed record TodoistInputCatalog(
    string? AccountName,
    IReadOnlyList<TodoistInputSourceOption> Projects,
    IReadOnlyList<TodoistInputSourceOption> Labels);

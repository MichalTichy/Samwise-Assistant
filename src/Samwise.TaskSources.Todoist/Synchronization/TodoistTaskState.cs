namespace Samwise.TaskSources.Todoist.Synchronization;

public sealed record TodoistTaskState(string State, TodoistTaskSnapshot? Task = null,
    string? AccountTimeZone = null, string? AccountUserId = null);

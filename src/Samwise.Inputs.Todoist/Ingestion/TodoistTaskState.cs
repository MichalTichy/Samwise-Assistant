namespace Samwise.Inputs.Todoist.Ingestion;

public sealed record TodoistTaskState(string State, TodoistTaskSnapshot? Task = null,
    string? AccountTimeZone = null, string? AccountUserId = null);

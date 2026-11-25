namespace Samwise.TaskSources.Todoist.Configuration;

public sealed record TodoistTaskSourceConfigurationSaveResult(bool Succeeded, IReadOnlyList<string> Errors);

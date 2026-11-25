namespace Samwise.TaskSources.Todoist.Usecases.Dismissal;

public sealed record TodoistTaskDismissalResult(bool Succeeded, string? TodoistTaskId = null, string? Error = null)
{
    public static TodoistTaskDismissalResult Failed(string error) => new(false, Error: error);
}

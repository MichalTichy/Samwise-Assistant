namespace Samwise.TaskSources.Todoist.Configuration;

/// <summary>A Todoist task the owner removed from Samwise; every occurrence and generation is excluded until restored.</summary>
public sealed class TodoistDismissedTask
{
    public string TaskId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    // The latest Samwise task at the time of removal, kept so its history stays reachable.
    public string? AgentTaskId { get; set; }
    public DateTimeOffset DismissedAt { get; set; }
}

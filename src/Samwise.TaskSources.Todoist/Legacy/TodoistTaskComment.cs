namespace Samwise.TaskSources.Todoist.Legacy;

public sealed class TodoistTaskComment
{
    public string CommentId { get; set; } = string.Empty;
    public DateTimeOffset? PostedAt { get; set; }
    public string Content { get; set; } = string.Empty;
}

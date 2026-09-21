namespace Samwise.Inputs.Todoist.InboundEvents;

public sealed class TodoistTaskComment
{
    public string CommentId { get; set; } = string.Empty;
    public DateTimeOffset? PostedAt { get; set; }
    public string Content { get; set; } = string.Empty;
}

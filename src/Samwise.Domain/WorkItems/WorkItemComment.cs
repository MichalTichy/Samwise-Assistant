namespace Samwise.Domain.WorkItems;

public enum CommentAuthorKind
{
    User,
    Samwise,
    System
}

public enum WorkItemCommentKind
{
    Context,
    PlanningFeedback,
    ExecutionUpdate
}

public sealed class WorkItemComment
{
    public string Id { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public CommentAuthorKind AuthorKind { get; set; }
    public WorkItemCommentKind Kind { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

namespace Samwise.Domain.Tasks;

public enum CommentAuthorKind
{
    User,
    Samwise,
    System
}

public enum TaskCommentKind
{
    Context,
    PlanningFeedback,
    DecisionReceipt,
    ExecutionUpdate
}

public sealed class TaskComment
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public string Body { get; set; } = string.Empty;
    public CommentAuthorKind AuthorKind { get; set; }
    public TaskCommentKind Kind { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

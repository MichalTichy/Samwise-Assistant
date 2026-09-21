using Samwise.Inputs.Todoist.InboundEvents;

namespace Samwise.Inputs.Todoist.Ingestion;

public sealed record TodoistTaskSnapshot(
    string TaskId,
    string Content,
    string Description,
    string ProjectId,
    string? ProjectName,
    IReadOnlyList<string> Labels,
    int Priority,
    TodoistDue Due,
    DateOnly? DeadlineDate,
    string Url);

public sealed record TodoistCommentSnapshot(string CommentId, DateTimeOffset? PostedAt, string Content);

public sealed record TodoistTaskReadResult(IReadOnlyList<TodoistTaskSnapshot> Tasks, string? AccountTimeZone);

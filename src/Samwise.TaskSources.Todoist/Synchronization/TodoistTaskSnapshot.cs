using Samwise.TaskSources.Todoist.Models;
using Samwise.TaskSources.Todoist.Legacy;

namespace Samwise.TaskSources.Todoist.Synchronization;

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
    string Url,
    int? CompletedCount = null,
    string? ResponsibleUserId = null);

public sealed record TodoistCommentSnapshot(string CommentId, DateTimeOffset? PostedAt, string Content);

public sealed record TodoistTaskReadResult(IReadOnlyList<TodoistTaskSnapshot> Tasks, string? AccountTimeZone,
    string? AccountUserId = null);

using Samwise.TaskSources.Todoist.Discovery;

namespace Samwise.TaskSources.Todoist.Synchronization;

public interface ITodoistTaskReader
{
    Task<TodoistTaskState> ReadTaskStateAsync(string taskId, CancellationToken cancellationToken = default);
    /// <summary>Reads all active tasks that have a due date, plus the account timezone.</summary>
    Task<TodoistTaskReadResult> ReadDueTasksAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TodoistCommentSnapshot>> ReadCommentsAsync(
        string taskId,
        CancellationToken cancellationToken = default);

    Task<TodoistSourceCatalog> GetCatalogAsync(CancellationToken cancellationToken = default);
}

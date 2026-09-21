using Samwise.Inputs.Todoist.Discovery;

namespace Samwise.Inputs.Todoist.Ingestion;

public interface ITodoistTaskReader
{
    /// <summary>Reads all active tasks that have a due date, plus the account timezone.</summary>
    Task<TodoistTaskReadResult> ReadDueTasksAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TodoistCommentSnapshot>> ReadCommentsAsync(
        string taskId,
        CancellationToken cancellationToken = default);

    Task<TodoistInputCatalog> GetCatalogAsync(CancellationToken cancellationToken = default);
}

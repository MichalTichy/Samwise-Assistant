using Samwise.TaskSources.Todoist.Synchronization;

namespace Samwise.TaskSources.Todoist.Synchronization;

internal interface ITodoistTaskSyncService
{
    Task<TodoistSynchronizationResult> SynchronizeAsync(string? externalTaskId, CancellationToken cancellationToken);
}

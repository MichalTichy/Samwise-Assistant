using Samwise.Inputs.Todoist.Ingestion;

namespace Samwise.Inputs.Todoist.Tasks;

internal interface ITodoistTaskSyncService
{
    Task<TodoistProcessingResult> SynchronizeAsync(string? externalTaskId, CancellationToken cancellationToken);
}

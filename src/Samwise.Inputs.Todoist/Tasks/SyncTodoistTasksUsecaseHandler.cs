using MediatR;
using Samwise.Inputs.Todoist.Ingestion;

namespace Samwise.Inputs.Todoist.Tasks;

internal sealed class SyncTodoistTasksUsecaseHandler(ITodoistTaskSyncService sync)
    : IRequestHandler<SyncTodoistTasksUsecase, TodoistProcessingResult>
{
    public Task<TodoistProcessingResult> Handle(SyncTodoistTasksUsecase request, CancellationToken cancellationToken) =>
        sync.SynchronizeAsync(request.ExternalTaskId, cancellationToken);
}

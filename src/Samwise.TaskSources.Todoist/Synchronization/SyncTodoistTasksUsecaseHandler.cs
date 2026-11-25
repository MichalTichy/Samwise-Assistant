using MediatR;
using Samwise.TaskSources.Todoist.Synchronization;

namespace Samwise.TaskSources.Todoist.Synchronization;

internal sealed class SyncTodoistTasksUsecaseHandler(ITodoistTaskSyncService sync)
    : IRequestHandler<SyncTodoistTasksUsecase, TodoistSynchronizationResult>
{
    public Task<TodoistSynchronizationResult> Handle(SyncTodoistTasksUsecase request, CancellationToken cancellationToken) =>
        sync.SynchronizeAsync(request.ExternalTaskId, cancellationToken);
}

using MediatR;
using Samwise.Domain.Tasks;
using Samwise.TaskSources.Todoist.Notifications;
using Samwise.TaskSources.Todoist.Resources;
using Samwise.TaskSources.Todoist.Synchronization;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.TaskSources.Todoist.Usecases.Dismissal;

internal sealed class RestoreTodoistTaskUsecaseHandler(
    IRepository<AgentTask> tasks,
    ITodoistSynchronizationLock processingLock,
    IPublisher publisher,
    TimeProvider clock)
    : IRequestHandler<RestoreTodoistTaskUsecase, TodoistTaskDismissalResult>
{
    public async Task<TodoistTaskDismissalResult> Handle(RestoreTodoistTaskUsecase request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TodoistTaskId))
            return TodoistTaskDismissalResult.Failed(TodoistTaskErrors.NotDismissed);

        var todoistTaskId = request.TodoistTaskId.Trim();
        using var lease = await processingLock.EnterAsync(cancellationToken);
        var latest = await tasks.GetBySpecAsync(new LatestTodoistTaskSpecification(todoistTaskId), cancellationToken);
        if (latest?.ExternalSource?.State != TodoistTaskRetirement.DismissedState)
            return TodoistTaskDismissalResult.Failed(TodoistTaskErrors.NotDismissed);

        // The task stays inactive until the next synchronization reads Todoist and reactivates or replaces it.
        latest.ExternalSource.State = TodoistTaskRetirement.RestoredState;
        latest.UpdatedAt = clock.GetUtcNow();
        await tasks.UpdateAsync(latest, cancellationToken);

        await publisher.Publish(new AgentTaskChanged(latest.Id), cancellationToken);
        await publisher.Publish(new TodoistTaskRestored(todoistTaskId), cancellationToken);
        return new TodoistTaskDismissalResult(true, todoistTaskId);
    }
}

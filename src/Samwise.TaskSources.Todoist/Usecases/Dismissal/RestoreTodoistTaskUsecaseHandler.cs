using MediatR;
using Samwise.Domain.Tasks;
using Samwise.TaskSources.Todoist.Configuration;
using Samwise.TaskSources.Todoist.Notifications;
using Samwise.TaskSources.Todoist.Resources;
using Samwise.TaskSources.Todoist.Synchronization;
using Shared.Infrastructure.Persistence.Marten.UnitOfWorks;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.TaskSources.Todoist.Usecases.Dismissal;

internal sealed class RestoreTodoistTaskUsecaseHandler(
    IRepository<TodoistTaskSourceConfiguration> configurations,
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
        var configuration = await configurations.GetRequiredByIdAsync(TodoistTaskSourceConfiguration.DocumentId, cancellationToken);
        if (!configuration.RestoreTask(todoistTaskId))
            return TodoistTaskDismissalResult.Failed(TodoistTaskErrors.NotDismissed);

        // The task stays inactive until the next synchronization reads Todoist and reactivates or replaces it.
        var latest = await tasks.GetBySpecAsync(new LatestTodoistTaskSpecification(todoistTaskId), cancellationToken);
        var relabel = latest?.ExternalSource is { IsActive: false, State: TodoistTaskRetirement.DismissedState };
        if (relabel)
        {
            latest!.ExternalSource!.State = TodoistTaskRetirement.RestoredState;
            latest.UpdatedAt = clock.GetUtcNow();
        }

        await using (var unit = new UnitOfWorkProvider())
        {
            await configurations.UpdateAsync(configuration, cancellationToken);
            if (relabel) await tasks.UpdateAsync(latest!, cancellationToken);
            await unit.CommitAsync();
        }

        if (relabel) await publisher.Publish(new AgentTaskChanged(latest!.Id), cancellationToken);
        await publisher.Publish(new TodoistTaskRestored(todoistTaskId), cancellationToken);
        return new TodoistTaskDismissalResult(true, todoistTaskId);
    }
}

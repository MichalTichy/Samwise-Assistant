using MediatR;
using Samwise.Domain.Tasks;
using Samwise.Domain.WorkItems;
using Samwise.Logic.WorkItems.Services;
using Samwise.TaskSources.Todoist.Notifications;
using Samwise.TaskSources.Todoist.Resources;
using Samwise.TaskSources.Todoist.Synchronization;
using Shared.Infrastructure.Persistence.Marten.UnitOfWorks;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.TaskSources.Todoist.Usecases.Dismissal;

internal sealed class DismissTodoistTaskUsecaseHandler(
    IRepository<AgentTask> tasks,
    IRepository<WorkItem> workItems,
    ITodoistSynchronizationLock processingLock,
    IWorkItemEventPublisher events,
    IPublisher publisher,
    TimeProvider clock)
    : IRequestHandler<DismissTodoistTaskUsecase, TodoistTaskDismissalResult>
{
    public async Task<TodoistTaskDismissalResult> Handle(DismissTodoistTaskUsecase request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AgentTaskId))
            return TodoistTaskDismissalResult.Failed(TodoistTaskErrors.TaskNotFound);

        // Synchronization updates the same task documents, so removal shares its lock to avoid a lost update.
        using var lease = await processingLock.EnterAsync(cancellationToken);
        var selected = await tasks.GetByIdAsync(request.AgentTaskId.Trim(), cancellationToken);
        if (selected is null)
            return TodoistTaskDismissalResult.Failed(TodoistTaskErrors.TaskNotFound);
        if (selected.ExternalSource is not { Provider: "Todoist" } source || string.IsNullOrWhiteSpace(source.TaskId))
            return TodoistTaskDismissalResult.Failed(TodoistTaskErrors.NotTodoistTask);

        var todoistTaskId = source.TaskId;
        // The removal is carried by the latest generation: synchronization derives every later occurrence from it.
        var latest = await tasks.GetBySpecAsync(new LatestTodoistTaskSpecification(todoistTaskId), cancellationToken) ?? selected;
        if (latest.ExternalSource!.State == TodoistTaskRetirement.DismissedState)
            return new TodoistTaskDismissalResult(true, todoistTaskId);

        var now = clock.GetUtcNow();
        WorkItem? pending = null;
        if (latest.ExternalSource.IsActive)
        {
            if (latest.WorkItemIds.LastOrDefault() is { } workItemId)
                pending = await workItems.GetRequiredByIdAsync(workItemId, cancellationToken);
            TodoistTaskRetirement.Retire(latest, pending, TodoistTaskRetirement.DismissedState, now);
        }
        else
        {
            // An already retired task has no delegated work left; only record why it stays out of Samwise.
            latest.ExternalSource.State = TodoistTaskRetirement.DismissedState;
            latest.ExternalSource.VerifiedAt = now;
            latest.UpdatedAt = now;
        }

        await using (var unit = new UnitOfWorkProvider())
        {
            await tasks.UpdateAsync(latest, cancellationToken);
            if (pending is not null) await workItems.UpdateAsync(pending, cancellationToken);
            await unit.CommitAsync();
        }

        if (pending is not null) await events.PublishAsync(pending);
        await publisher.Publish(new AgentTaskChanged(latest.Id), cancellationToken);
        await publisher.Publish(new TodoistTaskDismissed(todoistTaskId, latest.Id), cancellationToken);
        return new TodoistTaskDismissalResult(true, todoistTaskId);
    }
}

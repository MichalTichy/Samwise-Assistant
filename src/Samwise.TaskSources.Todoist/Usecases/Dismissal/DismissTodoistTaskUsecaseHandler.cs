using MediatR;
using Samwise.Domain.Tasks;
using Samwise.Domain.WorkItems;
using Samwise.Logic.WorkItems.Services;
using Samwise.TaskSources.Todoist.Configuration;
using Samwise.TaskSources.Todoist.Notifications;
using Samwise.TaskSources.Todoist.Resources;
using Samwise.TaskSources.Todoist.Synchronization;
using Shared.Infrastructure.Persistence.Marten.UnitOfWorks;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.TaskSources.Todoist.Usecases.Dismissal;

internal sealed class DismissTodoistTaskUsecaseHandler(
    IRepository<TodoistTaskSourceConfiguration> configurations,
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

        // Synchronization rewrites the whole configuration document, so removal shares its lock to avoid a lost update.
        using var lease = await processingLock.EnterAsync(cancellationToken);
        var selected = await tasks.GetByIdAsync(request.AgentTaskId.Trim(), cancellationToken);
        if (selected is null)
            return TodoistTaskDismissalResult.Failed(TodoistTaskErrors.TaskNotFound);
        if (selected.ExternalSource is not { Provider: "Todoist" } source || string.IsNullOrWhiteSpace(source.TaskId))
            return TodoistTaskDismissalResult.Failed(TodoistTaskErrors.NotTodoistTask);

        var now = clock.GetUtcNow();
        var todoistTaskId = source.TaskId;
        // Removal applies to the Todoist task, so it acts on the current generation even when an older occurrence was opened.
        var latest = await tasks.GetBySpecAsync(new LatestTodoistTaskSpecification(todoistTaskId), cancellationToken) ?? selected;
        var configuration = await configurations.GetRequiredByIdAsync(TodoistTaskSourceConfiguration.DocumentId, cancellationToken);
        configuration.DismissTask(todoistTaskId, latest.Title, latest.ExternalSource!.Url, latest.Id, now);

        WorkItem? pending = null;
        var taskChanged = latest.ExternalSource.State != TodoistTaskRetirement.DismissedState;
        if (latest.ExternalSource.IsActive)
        {
            if (latest.WorkItemIds.LastOrDefault() is { } workItemId)
                pending = await workItems.GetRequiredByIdAsync(workItemId, cancellationToken);
            TodoistTaskRetirement.Retire(latest, pending, TodoistTaskRetirement.DismissedState, now);
        }
        else if (taskChanged)
        {
            // An already retired task has no delegated work left; only mark why it stays out of Samwise.
            latest.ExternalSource.State = TodoistTaskRetirement.DismissedState;
            latest.ExternalSource.VerifiedAt = now;
            latest.UpdatedAt = now;
        }

        await using (var unit = new UnitOfWorkProvider())
        {
            await configurations.UpdateAsync(configuration, cancellationToken);
            if (taskChanged) await tasks.UpdateAsync(latest, cancellationToken);
            if (pending is not null) await workItems.UpdateAsync(pending, cancellationToken);
            await unit.CommitAsync();
        }

        if (pending is not null) await events.PublishAsync(pending);
        if (taskChanged) await publisher.Publish(new AgentTaskChanged(latest.Id), cancellationToken);
        await publisher.Publish(new TodoistTaskDismissed(todoistTaskId, latest.Id), cancellationToken);
        return new TodoistTaskDismissalResult(true, todoistTaskId);
    }
}

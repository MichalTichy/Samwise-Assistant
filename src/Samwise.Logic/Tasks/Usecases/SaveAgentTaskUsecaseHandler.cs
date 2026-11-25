using MediatR;
using Samwise.Domain.Tasks;
using Samwise.Domain.WorkItems;
using Shared.Infrastructure.Persistence.Repositories;
using Shared.Infrastructure.Persistence.Marten.UnitOfWorks;
using Samwise.Logic.WorkItems.Services;

namespace Samwise.Logic.Tasks.Usecases;

internal sealed class SaveAgentTaskUsecaseHandler(IRepository<AgentTask> repository,
    IRepository<WorkItem> workItems, IPublisher publisher, TimeProvider clock, IWorkItemEventPublisher eventPublisher)
    : IRequestHandler<SaveAgentTaskUsecase, AgentTask>
{
    public async Task<AgentTask> Handle(SaveAgentTaskUsecase request, CancellationToken cancellationToken)
    {
        var task = request.Id is null ? new AgentTask() :
            await repository.GetRequiredByIdAsync(request.Id, cancellationToken);
        if (task.Version != request.ExpectedVersion)
            throw new InvalidOperationException(Samwise.Domain.Tasks.TaskErrors.StaleTask);
        if (task.ExternalSource is { } source &&
            (!source.IsActive || request.Title.Trim() != task.Title || request.Instructions.Trim() != task.Instructions))
            throw new InvalidOperationException(TaskErrors.ExternalSourceOwned);
        if (request.NextRunAt is { } next && next <= clock.GetUtcNow())
            throw new ArgumentException(Samwise.Domain.Tasks.TaskErrors.FutureCheck);
        var last = task.WorkItemIds.LastOrDefault();
        var workItem = last is null ? null : await workItems.GetRequiredByIdAsync(last, cancellationToken);
        if (!request.IsOwnerEdit)
        {
            var executing = request.ExecutingWorkItemId is { } executingId
                ? await workItems.GetByIdAsync(executingId, cancellationToken) : null;
            if (executing?.Status != WorkItemStatus.Executing ||
                (workItem is not null && workItem.Status is not (WorkItemStatus.Completed or WorkItemStatus.Discarded) &&
                    (executing.AgentTaskId != task.Id || last != executing.Id)))
                throw new InvalidOperationException(TaskErrors.ResolveExecution);
        }
        if (workItem?.Status == WorkItemStatus.OutcomeUnknown ||
            (request.IsOwnerEdit && workItem?.Status == WorkItemStatus.Executing))
            throw new InvalidOperationException(Samwise.Domain.Tasks.TaskErrors.ResolveExecution);
        if (request.NextRunAt is null && request.Checklist.Any(item => !item.IsCompleted) &&
            (request.IsOwnerEdit || workItem is null || workItem.Status is WorkItemStatus.Completed or WorkItemStatus.Discarded))
            throw new ArgumentException(Samwise.Domain.Tasks.TaskErrors.MissingNext);
        task.Update(request.Title, request.Instructions, request.Checklist, request.NextRunAt, clock.GetUtcNow());
        if (request.Id is null)
        {
            if (request.OriginWorkItemId is { } origin)
                await workItems.GetRequiredByIdAsync(origin, cancellationToken);
            task.OriginWorkItemId = request.IsOwnerEdit ? request.OriginWorkItemId : request.ExecutingWorkItemId;
        }
        var invalidate = request.IsOwnerEdit && workItem is not null &&
            workItem.Status is not (WorkItemStatus.Completed or WorkItemStatus.Discarded);
        if (invalidate) workItem!.Discard(clock.GetUtcNow(), "Task edit", Samwise.Domain.Tasks.TaskErrors.RetiredPlan);
        await PersistAsync(task, request.Id is null, invalidate ? workItem : null, cancellationToken);
        if (invalidate) await eventPublisher.PublishAsync(workItem!);
        await publisher.Publish(new AgentTaskChanged(task.Id), cancellationToken);
        return task;
    }

    private async Task PersistAsync(AgentTask task, bool isNew, WorkItem? invalidated, CancellationToken cancellationToken)
    {
        await using var unitOfWork = new UnitOfWorkProvider();
        if (isNew) await repository.AddAsync(task, cancellationToken);
        else await repository.UpdateAsync(task, cancellationToken);
        if (invalidated is not null) await workItems.UpdateAsync(invalidated, cancellationToken);
        await unitOfWork.CommitAsync();
    }
}

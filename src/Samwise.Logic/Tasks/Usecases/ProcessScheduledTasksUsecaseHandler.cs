using MediatR;
using Microsoft.Extensions.Logging;
using Samwise.Domain.Tasks;
using Samwise.Domain.WorkItems;
using Samwise.Domain.Inbox;
using Samwise.Logic.Inbox.Services;
using Samwise.Logic.Tasks.Specifications;
using Samwise.Logic.WorkItems.Services;
using Shared.Infrastructure.Persistence.Marten.UnitOfWorks;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Tasks.Usecases;

internal sealed class ProcessScheduledTasksUsecaseHandler(IRepository<AgentRoutine> routines,
    IRepository<AgentTask> tasks, IRepository<WorkItem> workItems,
    IRepository<InboundEvent> inputs, IInboundEventProcessingQueue queue, IWorkItemEventPublisher events,
    IPublisher publisher, TimeProvider clock, ILogger<ProcessScheduledTasksUsecaseHandler> logger,
    IEnumerable<IExternalTaskRunGate>? sourceGates = null)
    : IRequestHandler<ProcessScheduledTasksUsecase>
{
    public async Task Handle(ProcessScheduledTasksUsecase request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        foreach (var routine in await routines.ListAsync(new DueAgentRoutinesSpecification(now), cancellationToken))
        {
            try
            {
                await GenerateNextAsync(routine, now, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Generating task occurrence for {RoutineId} failed", routine.Id);
            }
        }

        foreach (var candidate in await tasks.ListAsync(new DueAgentTasksSpecification(now), cancellationToken))
        {
            try
            {
                var task = candidate;
                if (task.ExternalSource is { } source)
                {
                    var gate = sourceGates?.SingleOrDefault(item => item.Provider == source.Provider);
                    if (gate is null || !await gate.RefreshBeforeRunAsync(task.Id, cancellationToken)) continue;
                    task = await tasks.GetRequiredByIdAsync(task.Id, cancellationToken);
                    if (task.ExternalSource is not { IsActive: true } || task.CompletedAt is not null ||
                        task.NextRunAt is null || task.NextRunAt > clock.GetUtcNow()) continue;
                }
                if (task.RoutineId is { } routineId &&
                    await routines.GetByIdAsync(routineId, cancellationToken) is not { IsEnabled: true }) continue;
                // A question, pending approval, failure or uncertain write needs resolution, not a second run.
                if ((task.WorkItemIds.LastOrDefault() ?? task.OriginWorkItemId) is { } last)
                {
                    var previous = await workItems.GetRequiredByIdAsync(last, cancellationToken);
                    if (previous.Status is not (WorkItemStatus.Completed or WorkItemStatus.Discarded)) continue;
                }
                var workItem = WorkItem.CreateFromInbound(task.Instructions, task.Title, now);
                workItem.AgentTaskId = task.Id;
                var input = new ScheduledTaskInboundEvent
                {
                    Id = Guid.NewGuid().ToString(), TaskId = task.Id, WorkItemId = workItem.Id,
                    Instructions = task.Instructions, ReceivedAt = now,
                    Status = InboundEventStatus.Pending,
                    DedupeKey = InboundEvent.CreateDedupeKey("Samwise task", workItem.Id)
                };
                input.Outcomes.Add(new InboundEventOutcome
                {
                    Id = Guid.NewGuid().ToString(), Kind = InboundEventOutcomeKind.CreatedWorkItem,
                    WorkItemId = workItem.Id, RelevantText = task.Instructions,
                    Explanation = "Scheduled internal task; proceed directly to planning."
                });
                workItem.Sources.Add(new WorkItemSourceReference
                {
                    InboundEventId = input.Id, Source = input.Source, ExternalId = input.ExternalId, AddedAt = now
                });
                task.StartRun(workItem.Id, now);
                await PersistRunAsync(task, workItem, input, cancellationToken);
                await events.PublishAsync(workItem);
                await publisher.Publish(new AgentTaskChanged(task.Id), cancellationToken);
                queue.Enqueue(input.Id);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Starting scheduled task {TaskId} failed", candidate.Id);
            }
        }
    }

    private async Task GenerateNextAsync(AgentRoutine routine, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Scope ambient transactions to a separate async operation; never reuse a committed context in the tick.
        await using var unitOfWork = new UnitOfWorkProvider();
        routine.NextOccurrenceAt = TaskRecurrence.Next(routine.Cron, routine.TimeZoneId, now);
        var next = routine.CreateOccurrence();
        if (await tasks.GetByIdAsync(next.Id, cancellationToken) is null)
            await tasks.AddAsync(next, cancellationToken);
        await routines.UpdateAsync(routine, cancellationToken);
        await unitOfWork.CommitAsync();
    }

    private async Task PersistRunAsync(AgentTask task, WorkItem workItem, InboundEvent input, CancellationToken cancellationToken)
    {
        await using var unitOfWork = new UnitOfWorkProvider();
        await workItems.AddAsync(workItem, cancellationToken);
        await inputs.AddAsync(input, cancellationToken);
        await tasks.UpdateAsync(task, cancellationToken); // Version check arbitrates concurrent schedulers.
        await unitOfWork.CommitAsync();
    }
}

using MediatR;
using Samwise.Domain.Tasks;
using Shared.Infrastructure.Persistence.Repositories;
using Shared.Infrastructure.Persistence.Marten.UnitOfWorks;

namespace Samwise.Logic.Tasks.Usecases;

internal sealed class SaveAgentRoutineUsecaseHandler(IRepository<AgentRoutine> repository,
    IRepository<AgentTask> tasks, IPublisher publisher, TimeProvider clock)
    : IRequestHandler<SaveAgentRoutineUsecase, AgentRoutine>
{
    public async Task<AgentRoutine> Handle(SaveAgentRoutineUsecase request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var validated = new AgentTask();
        validated.Update(request.Title, request.Instructions,
            request.Checklist.Select(item => item.Copy(reset: true)), null, now);
        var next = TaskRecurrence.Next(request.Cron, request.TimeZoneId, now);
        var routine = request.Id is null ? new AgentRoutine() :
            await repository.GetRequiredByIdAsync(request.Id, cancellationToken);
        if (routine.Version != request.ExpectedVersion)
            throw new InvalidOperationException(Samwise.Domain.Tasks.TaskErrors.StaleRoutine);
        routine.Title = validated.Title;
        routine.Instructions = validated.Instructions;
        routine.Checklist = validated.Checklist;
        routine.Cron = request.Cron;
        routine.TimeZoneId = request.TimeZoneId;
        routine.IsEnabled = request.IsEnabled;
        // Existing instances remain independent; the cursor must follow the current recurrence.
        routine.NextOccurrenceAt = next;
        await PersistAsync(routine, request.Id is null, cancellationToken);
        await publisher.Publish(new AgentRoutineChanged(routine.Id), cancellationToken);
        return routine;
    }

    private async Task PersistAsync(AgentRoutine routine, bool isNew, CancellationToken cancellationToken)
    {
        await using var unitOfWork = new UnitOfWorkProvider();
        var occurrence = routine.CreateOccurrence();
        if (await tasks.GetByIdAsync(occurrence.Id, cancellationToken) is null)
            await tasks.AddAsync(occurrence, cancellationToken);
        if (isNew) await repository.AddAsync(routine, cancellationToken);
        else await repository.UpdateAsync(routine, cancellationToken);
        await unitOfWork.CommitAsync();
    }
}

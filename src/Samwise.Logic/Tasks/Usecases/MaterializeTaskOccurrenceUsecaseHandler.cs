using MediatR;
using Samwise.Domain.Tasks;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Tasks.Usecases;

internal sealed class MaterializeTaskOccurrenceUsecaseHandler(IReadOnlyRepository<AgentRoutine> routines,
    IRepository<AgentTask> tasks, IPublisher publisher) : IRequestHandler<MaterializeTaskOccurrenceUsecase, AgentTask>
{
    public async Task<AgentTask> Handle(MaterializeTaskOccurrenceUsecase request, CancellationToken cancellationToken)
    {
        var routine = await routines.GetRequiredByIdAsync(request.RoutineId, cancellationToken);
        var time = request.OccurrenceAt.ToUniversalTime();
        if (TaskRecurrence.Next(routine.Cron, routine.TimeZoneId, time.AddTicks(-1)) != time)
            throw new ArgumentException(Samwise.Domain.Tasks.TaskErrors.InvalidOccurrence);
        routine.NextOccurrenceAt = time; // Detached copy; do not move the recurring template's cursor.
        var occurrence = routine.CreateOccurrence();
        var existing = await tasks.GetByIdAsync(occurrence.Id, cancellationToken);
        if (existing is not null) return existing;
        await tasks.AddAsync(occurrence, cancellationToken);
        await publisher.Publish(new AgentTaskChanged(occurrence.Id), cancellationToken);
        return occurrence;
    }
}

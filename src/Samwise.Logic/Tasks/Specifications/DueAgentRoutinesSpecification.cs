using Marten;
using Samwise.Domain.Tasks;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Logic.Tasks.Specifications;

internal sealed class DueAgentRoutinesSpecification(DateTimeOffset now) : IListSpecification<AgentRoutine>
{
    public async Task<IReadOnlyList<AgentRoutine>> ApplyAsync(IQueryable<AgentRoutine> queryable,
        CancellationToken cancellationToken = default) => await queryable
        .Where(routine => routine.IsEnabled && routine.NextOccurrenceAt != null && routine.NextOccurrenceAt <= now)
        .OrderBy(routine => routine.NextOccurrenceAt).Take(50).ToListAsync(cancellationToken);
}

using Marten;
using Samwise.Domain.Tasks;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Logic.Tasks.Specifications;

public sealed class AgentRoutinesSpecification : IListSpecification<AgentRoutine>
{
    public async Task<IReadOnlyList<AgentRoutine>> ApplyAsync(IQueryable<AgentRoutine> queryable,
        CancellationToken cancellationToken = default) => await queryable
        .OrderBy(routine => routine.Title).Take(200).ToListAsync(cancellationToken);
}

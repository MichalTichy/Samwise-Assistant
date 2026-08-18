using Marten;
using Samwise.Domain.Tasks;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Infrastructure.Persistence.Marten.Specifications;

internal sealed class TaskDecisionsSpecification(Guid taskId) : IListSpecification<PlanDecision>
{
    public async Task<IReadOnlyList<PlanDecision>> ApplyAsync(
        IQueryable<PlanDecision> queryable,
        CancellationToken cancellationToken = default) =>
        await queryable
            .Where(decision => decision.TaskId == taskId)
            .OrderByDescending(decision => decision.DecidedAt)
            .ToListAsync(cancellationToken);
}

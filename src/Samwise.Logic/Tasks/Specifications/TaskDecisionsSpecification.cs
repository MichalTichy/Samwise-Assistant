using Marten;
using Samwise.Domain.Tasks;
using Shared.Infrastructure.Persistence.Specifications;
using Task = System.Threading.Tasks.Task;

namespace Samwise.Logic.Tasks.Specifications;

internal sealed class TaskDecisionsSpecification(string taskId) : IListSpecification<PlanDecision>
{
    public async Task<IReadOnlyList<PlanDecision>> ApplyAsync(
        IQueryable<PlanDecision> queryable,
        CancellationToken cancellationToken = default) =>
        await queryable
            .Where(decision => decision.TaskId == taskId)
            .OrderByDescending(decision => decision.DecidedAt)
            .ToListAsync(cancellationToken);
}

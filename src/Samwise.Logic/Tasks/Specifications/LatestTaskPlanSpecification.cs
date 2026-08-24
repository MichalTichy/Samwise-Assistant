using Marten;
using Samwise.Domain.Tasks;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Logic.Tasks.Specifications;

internal sealed class LatestTaskPlanSpecification(string taskId) : ISpecification<TaskPlan>
{
    public async Task<TaskPlan?> ApplyAsync(
        IQueryable<TaskPlan> queryable,
        CancellationToken cancellationToken = default) =>
        await queryable
            .Where(plan => plan.TaskId == taskId)
            .OrderByDescending(plan => plan.PlanNumber)
            .FirstOrDefaultAsync(cancellationToken);
}

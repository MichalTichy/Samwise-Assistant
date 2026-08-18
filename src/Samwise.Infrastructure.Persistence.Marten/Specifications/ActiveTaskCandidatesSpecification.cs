using Marten;
using Samwise.Domain.Tasks;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Infrastructure.Persistence.Marten.Specifications;

internal sealed class ActiveTaskCandidatesSpecification(int limit = 100) : IListSpecification<SamwiseTask>
{
    public async Task<IReadOnlyList<SamwiseTask>> ApplyAsync(
        IQueryable<SamwiseTask> queryable,
        CancellationToken cancellationToken = default) =>
        await queryable
            .Where(task =>
                task.Status != SamwiseTaskStatus.Completed &&
                task.Status != SamwiseTaskStatus.Discarded)
            .OrderByDescending(task => task.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
}

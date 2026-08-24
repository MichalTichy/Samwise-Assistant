using Marten;
using Samwise.Domain.Tasks;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Infrastructure.Persistence.Marten.Specifications;

internal sealed class BoardTasksSpecification(bool includeArchived) : IListSpecification<SamwiseTask>
{
    public async Task<IReadOnlyList<SamwiseTask>> ApplyAsync(
        IQueryable<SamwiseTask> queryable,
        CancellationToken cancellationToken = default)
    {
        if (!includeArchived)
        {
            queryable = queryable.Where(task =>
                task.Status != SamwiseTaskStatus.Completed &&
                task.Status != SamwiseTaskStatus.Discarded);
        }

        return await queryable
            .OrderByDescending(task => task.UpdatedAt)
            .ToListAsync(cancellationToken);
    }
}

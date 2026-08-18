using Marten;
using Samwise.Domain.Tasks;
using Shared.Infrastructure.Persistence.Specifications;
using DomainTask = Samwise.Domain.Tasks.Task;
using Task = System.Threading.Tasks.Task;

namespace Samwise.Infrastructure.Persistence.Marten.Specifications;

internal sealed class BoardTasksSpecification(bool includeArchived) : IListSpecification<DomainTask>
{
    public async Task<IReadOnlyList<DomainTask>> ApplyAsync(
        IQueryable<DomainTask> queryable,
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

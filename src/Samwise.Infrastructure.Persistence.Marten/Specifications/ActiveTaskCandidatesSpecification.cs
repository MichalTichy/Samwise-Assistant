using Marten;
using Samwise.Domain.Tasks;
using Shared.Infrastructure.Persistence.Specifications;
using DomainTask = Samwise.Domain.Tasks.Task;
using Task = System.Threading.Tasks.Task;

namespace Samwise.Infrastructure.Persistence.Marten.Specifications;

internal sealed class ActiveTaskCandidatesSpecification(int limit = 100) : IListSpecification<DomainTask>
{
    public async Task<IReadOnlyList<DomainTask>> ApplyAsync(
        IQueryable<DomainTask> queryable,
        CancellationToken cancellationToken = default) =>
        await queryable
            .Where(task =>
                task.Status != SamwiseTaskStatus.Completed &&
                task.Status != SamwiseTaskStatus.Discarded)
            .OrderByDescending(task => task.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
}

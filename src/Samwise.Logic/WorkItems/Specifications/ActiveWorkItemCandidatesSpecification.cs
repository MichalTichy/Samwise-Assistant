using Marten;
using Samwise.Domain.WorkItems;
using Shared.Infrastructure.Persistence.Specifications;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;
using Task = System.Threading.Tasks.Task;
using WorkItemStatus = Samwise.Domain.WorkItems.WorkItemStatus;

namespace Samwise.Logic.WorkItems.Specifications;

internal sealed class ActiveWorkItemCandidatesSpecification(int limit = 100) : IListSpecification<DomainWorkItem>
{
    public async Task<IReadOnlyList<DomainWorkItem>> ApplyAsync(
        IQueryable<DomainWorkItem> queryable,
        CancellationToken cancellationToken = default) =>
        await queryable
            .Where(workItem =>
                workItem.Status != WorkItemStatus.Completed &&
                workItem.Status != WorkItemStatus.Discarded)
            .OrderByDescending(workItem => workItem.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
}

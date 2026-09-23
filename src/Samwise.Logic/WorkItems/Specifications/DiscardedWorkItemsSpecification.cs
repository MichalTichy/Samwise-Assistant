using Marten;
using Shared.Infrastructure.Persistence.Specifications;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;
using Task = System.Threading.Tasks.Task;
using WorkItemStatus = Samwise.Domain.WorkItems.WorkItemStatus;

namespace Samwise.Logic.WorkItems.Specifications;

public sealed class DiscardedWorkItemsSpecification : IListSpecification<DomainWorkItem>
{
    public async Task<IReadOnlyList<DomainWorkItem>> ApplyAsync(
        IQueryable<DomainWorkItem> queryable,
        CancellationToken cancellationToken = default)
    {
        return await queryable
            .Where(workItem => workItem.Status == WorkItemStatus.Discarded)
            .OrderByDescending(workItem => workItem.UpdatedAt)
            .ToListAsync(cancellationToken);
    }
}

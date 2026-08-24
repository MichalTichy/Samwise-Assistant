using Marten;
using Samwise.Domain.WorkItems;
using Shared.Infrastructure.Persistence.Specifications;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;
using Task = System.Threading.Tasks.Task;
using WorkItemStatus = Samwise.Domain.WorkItems.WorkItemStatus;

namespace Samwise.Logic.WorkItems.Specifications;

public sealed class BoardWorkItemsSpecification(bool includeArchived) : IListSpecification<DomainWorkItem>
{
    public async Task<IReadOnlyList<DomainWorkItem>> ApplyAsync(
        IQueryable<DomainWorkItem> queryable,
        CancellationToken cancellationToken = default)
    {
        if (!includeArchived)
        {
            queryable = queryable.Where(workItem =>
                workItem.Status != WorkItemStatus.Completed &&
                workItem.Status != WorkItemStatus.Discarded);
        }

        return await queryable
            .OrderByDescending(workItem => workItem.UpdatedAt)
            .ToListAsync(cancellationToken);
    }
}

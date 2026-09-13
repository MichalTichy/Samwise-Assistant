using Marten;
using Samwise.Domain.WorkItems;
using Shared.Infrastructure.Persistence.Specifications;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;

namespace Samwise.Logic.WorkItems.Specifications;

public sealed class RecentWorkItemsSpecification(
    WorkItemStatus? status,
    int limit) : IListSpecification<DomainWorkItem>
{
    public async Task<IReadOnlyList<DomainWorkItem>> ApplyAsync(
        IQueryable<DomainWorkItem> queryable,
        CancellationToken cancellationToken = default)
    {
        if (status.HasValue)
        {
            queryable = queryable.Where(workItem => workItem.Status == status.Value);
        }

        return await queryable
            .OrderByDescending(workItem => workItem.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}

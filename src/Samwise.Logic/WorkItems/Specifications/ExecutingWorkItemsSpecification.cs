using Marten;
using Samwise.Domain.WorkItems;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Logic.WorkItems.Specifications;

internal sealed class ExecutingWorkItemsSpecification : IListSpecification<WorkItem, WorkItem>
{
    public async Task<IReadOnlyList<WorkItem>> ApplyAsync(IQueryable<WorkItem> queryable, CancellationToken cancellationToken = default) =>
        await queryable.Where(item => item.Status == WorkItemStatus.Executing).ToListAsync(cancellationToken);
}

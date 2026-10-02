using Marten;
using Samwise.Domain.WorkItems;
using Shared.Infrastructure.Persistence.Specifications;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;
using WorkItemStatus = Samwise.Domain.WorkItems.WorkItemStatus;

namespace Samwise.Logic.Notifications.Specifications;

public sealed class AutoExecutedWorkItemsSpecification(
    DateTimeOffset from,
    DateTimeOffset until) : IListSpecification<DomainWorkItem>
{
    public async Task<IReadOnlyList<DomainWorkItem>> ApplyAsync(
        IQueryable<DomainWorkItem> queryable,
        CancellationToken cancellationToken = default)
    {
        var completed = await queryable
            .Where(workItem => workItem.Status == WorkItemStatus.Completed &&
                               workItem.CompletedAt >= from &&
                               workItem.CompletedAt < until)
            .OrderByDescending(workItem => workItem.CompletedAt)
            .ToListAsync(cancellationToken);

        return completed.Where(workItem => workItem.WasAutomaticallyExecuted()).ToList();
    }
}

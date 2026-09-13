using Marten;
using Samwise.Domain.Inbox;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Logic.Inbox.Specifications;

public sealed class RecentInboxEventsSpecification(int limit) : IListSpecification<InboundEvent>
{
    public async Task<IReadOnlyList<InboundEvent>> ApplyAsync(
        IQueryable<InboundEvent> queryable,
        CancellationToken cancellationToken = default) =>
        await queryable
            .OrderByDescending(item => item.ReceivedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
}

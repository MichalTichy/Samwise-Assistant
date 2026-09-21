using Marten;
using Samwise.Domain.Inbox;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Logic.Inbox.Specifications;

public sealed class PendingInboundEventsSpecification : IListSpecification<InboundEvent>
{
    public async Task<IReadOnlyList<InboundEvent>> ApplyAsync(
        IQueryable<InboundEvent> queryable,
        CancellationToken cancellationToken = default) =>
        await queryable
            .Where(item => item.Status == InboundEventStatus.Pending)
            .OrderBy(item => item.ReceivedAt)
            .ToListAsync(cancellationToken);
}

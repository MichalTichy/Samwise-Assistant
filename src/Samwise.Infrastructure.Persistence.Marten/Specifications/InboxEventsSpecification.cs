using Marten;
using Samwise.Domain.Inbox;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Infrastructure.Persistence.Marten.Specifications;

internal sealed class InboxEventsSpecification : IListSpecification<InboundEvent>
{
    public async Task<IReadOnlyList<InboundEvent>> ApplyAsync(
        IQueryable<InboundEvent> queryable,
        CancellationToken cancellationToken = default) =>
        await queryable
            .OrderByDescending(item => item.ReceivedAt)
            .ToListAsync(cancellationToken);
}

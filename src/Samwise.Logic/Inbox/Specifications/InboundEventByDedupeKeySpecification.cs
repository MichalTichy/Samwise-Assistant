using Marten;
using Samwise.Domain.Inbox;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Logic.Inbox.Specifications;

internal sealed class InboundEventByDedupeKeySpecification(string dedupeKey)
    : ISpecification<InboundEvent>
{
    public async Task<InboundEvent?> ApplyAsync(
        IQueryable<InboundEvent> queryable,
        CancellationToken cancellationToken = default) =>
        await queryable.FirstOrDefaultAsync(item => item.DedupeKey == dedupeKey, cancellationToken);
}

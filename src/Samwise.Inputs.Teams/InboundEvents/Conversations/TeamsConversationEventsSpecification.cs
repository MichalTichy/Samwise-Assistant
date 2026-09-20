using Marten;
using Samwise.Domain.Inbox;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Inputs.Teams.InboundEvents.Conversations;

internal sealed class TeamsConversationEventsSpecification(string contextKey)
    : IListSpecification<InboundEvent>
{
    public async Task<IReadOnlyList<InboundEvent>> ApplyAsync(
        IQueryable<InboundEvent> queryable,
        CancellationToken cancellationToken = default) =>
        await queryable
            .Where(item => item.ContextKey == contextKey)
            .OrderByDescending(item => item.ReceivedAt)
            .ToListAsync(cancellationToken);
}

using Marten;
using Samwise.Domain.Agents;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Logic.WorkItems.Specifications;

public sealed class WorkItemInvocationsSpecification(string workItemId, int limit = 10)
    : IListSpecification<AgentInvocation>
{
    public async Task<IReadOnlyList<AgentInvocation>> ApplyAsync(
        IQueryable<AgentInvocation> queryable,
        CancellationToken cancellationToken = default) =>
        await queryable
            .Where(invocation => invocation.WorkItemId == workItemId)
            .OrderByDescending(invocation => invocation.StartedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
}

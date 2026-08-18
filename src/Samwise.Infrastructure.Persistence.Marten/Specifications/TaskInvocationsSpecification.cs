using Marten;
using Samwise.Domain.Agents;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Infrastructure.Persistence.Marten.Specifications;

internal sealed class TaskInvocationsSpecification(Guid taskId, int limit = 10)
    : IListSpecification<AgentInvocation>
{
    public async Task<IReadOnlyList<AgentInvocation>> ApplyAsync(
        IQueryable<AgentInvocation> queryable,
        CancellationToken cancellationToken = default) =>
        await queryable
            .Where(invocation => invocation.TaskId == taskId)
            .OrderByDescending(invocation => invocation.StartedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
}

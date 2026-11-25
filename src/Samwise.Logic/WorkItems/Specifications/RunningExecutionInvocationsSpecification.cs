using Marten;
using Samwise.Domain.Agents;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Logic.WorkItems.Specifications;

internal sealed class RunningExecutionInvocationsSpecification(string workItemId) : IListSpecification<AgentInvocation, AgentInvocation>
{
    public async Task<IReadOnlyList<AgentInvocation>> ApplyAsync(IQueryable<AgentInvocation> queryable, CancellationToken cancellationToken = default) =>
        await queryable.Where(item => item.WorkItemId == workItemId && item.Status == AgentInvocationStatus.Running &&
            (item.Profile == "Execution" || item.Profile == "Auto-approved execution")).ToListAsync(cancellationToken);
}

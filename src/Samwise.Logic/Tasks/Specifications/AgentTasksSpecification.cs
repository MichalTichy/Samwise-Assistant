using Marten;
using Samwise.Domain.Tasks;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Logic.Tasks.Specifications;

public sealed class AgentTasksSpecification(bool includeCompleted = false) : IListSpecification<AgentTask>
{
    public async Task<IReadOnlyList<AgentTask>> ApplyAsync(IQueryable<AgentTask> queryable,
        CancellationToken cancellationToken = default) => await queryable
        .Where(task => includeCompleted || task.CompletedAt == null)
        .OrderByDescending(task => task.UpdatedAt).Take(200).ToListAsync(cancellationToken);
}

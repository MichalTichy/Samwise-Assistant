using Marten;
using Samwise.Domain.Tasks;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Logic.Tasks.Specifications;

internal sealed class DueAgentTasksSpecification(DateTimeOffset now) : IListSpecification<AgentTask>
{
    public async Task<IReadOnlyList<AgentTask>> ApplyAsync(IQueryable<AgentTask> queryable,
        CancellationToken cancellationToken = default) => await queryable
        .Where(task => task.CompletedAt == null && task.NextRunAt != null && task.NextRunAt <= now &&
            (task.ExternalSource == null || task.ExternalSource.IsActive))
        .OrderBy(task => task.NextRunAt).Take(50).ToListAsync(cancellationToken);
}

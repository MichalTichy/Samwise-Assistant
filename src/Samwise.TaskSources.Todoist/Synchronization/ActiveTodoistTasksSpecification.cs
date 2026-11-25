using Marten;
using Samwise.Domain.Tasks;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.TaskSources.Todoist.Synchronization;

internal sealed class ActiveTodoistTasksSpecification : IListSpecification<AgentTask>
{
    public async Task<IReadOnlyList<AgentTask>> ApplyAsync(IQueryable<AgentTask> queryable,
        CancellationToken cancellationToken = default) => await queryable
        .Where(task => task.ExternalSource != null && task.ExternalSource.Provider == "Todoist" && task.ExternalSource.IsActive)
        .ToListAsync(cancellationToken);
}

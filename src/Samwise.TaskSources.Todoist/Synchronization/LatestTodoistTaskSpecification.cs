using Marten;
using Samwise.Domain.Tasks;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.TaskSources.Todoist.Synchronization;

internal sealed class LatestTodoistTaskSpecification(string taskId) : ISpecification<AgentTask>
{
    public Task<AgentTask?> ApplyAsync(IQueryable<AgentTask> queryable, CancellationToken cancellationToken = default) =>
        queryable.Where(task => task.ExternalSource != null && task.ExternalSource.Provider == "Todoist" &&
            task.ExternalSource.TaskId == taskId).OrderByDescending(task => task.ExternalSource!.Generation)
            .FirstOrDefaultAsync(cancellationToken);
}

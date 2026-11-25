using Marten;
using Samwise.Domain.Tasks;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Web.Pages.Tasks.Sources.Todoist;

/// <summary>Todoist tasks the owner removed; only the latest generation carries the removal, so each appears once.</summary>
public sealed class DismissedTodoistTasksSpecification : IListSpecification<AgentTask, DismissedTodoistTask>
{
    public async Task<IReadOnlyList<DismissedTodoistTask>> ApplyAsync(IQueryable<AgentTask> queryable,
        CancellationToken cancellationToken = default) => await queryable
        .Where(task => task.ExternalSource != null && task.ExternalSource.Provider == "Todoist" &&
            task.ExternalSource.State == "dismissed")
        // A removed task can no longer be edited, so its last update is the removal.
        .OrderByDescending(task => task.UpdatedAt)
        .Select(task => new DismissedTodoistTask(task.Id, task.ExternalSource!.TaskId, task.Title,
            task.ExternalSource.Url, task.UpdatedAt))
        .ToListAsync(cancellationToken);
}

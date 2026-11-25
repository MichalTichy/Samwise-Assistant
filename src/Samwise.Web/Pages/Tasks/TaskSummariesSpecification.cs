using Marten;
using Samwise.Domain.Tasks;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Web.Pages.Tasks;

public sealed class TaskSummariesSpecification(bool includeCompleted, DateTimeOffset? from = null,
    DateTimeOffset? to = null) : IListSpecification<AgentTask, TaskSummary>
{
    public async Task<IReadOnlyList<TaskSummary>> ApplyAsync(IQueryable<AgentTask> queryable,
        CancellationToken cancellationToken = default)
    {
        if (from is { } start && to is { } end)
            queryable = queryable.Where(task =>
                (task.NextRunAt >= start && task.NextRunAt < end) ||
                (task.LastRunAt >= start && task.LastRunAt < end) ||
                (task.OccurrenceAt >= start && task.OccurrenceAt < end) ||
                (task.ExternalSource != null && task.ExternalSource.DueAt >= start && task.ExternalSource.DueAt < end));
        return await queryable.Where(task => includeCompleted || task.CompletedAt == null)
        .OrderByDescending(task => task.UpdatedAt)
        .Select(task => new TaskSummary(task.Id, task.Title, task.NextRunAt, task.CompletedAt,
            task.Checklist.Count, task.Checklist.Count(item => item.IsCompleted), task.LastRunAt,
            task.RoutineId, task.OccurrenceAt, task.ExternalSource == null ? null : task.ExternalSource.State,
            task.ExternalSource == null ? null : task.ExternalSource.DueAt)).ToListAsync(cancellationToken);
    }
}

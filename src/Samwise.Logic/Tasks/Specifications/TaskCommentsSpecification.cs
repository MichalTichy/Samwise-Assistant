using Marten;
using Samwise.Domain.Tasks;
using Shared.Infrastructure.Persistence.Specifications;
using Task = System.Threading.Tasks.Task;

namespace Samwise.Logic.Tasks.Specifications;

internal sealed class TaskCommentsSpecification(string taskId) : IListSpecification<TaskComment>
{
    public async Task<IReadOnlyList<TaskComment>> ApplyAsync(
        IQueryable<TaskComment> queryable,
        CancellationToken cancellationToken = default) =>
        await queryable
            .Where(comment => comment.TaskId == taskId)
            .OrderBy(comment => comment.CreatedAt)
            .ToListAsync(cancellationToken);
}

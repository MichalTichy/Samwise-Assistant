using Marten;
using Samwise.Domain.Tasks;
using Shared.Infrastructure.Persistence.Specifications;
using Task = System.Threading.Tasks.Task;

namespace Samwise.Infrastructure.Persistence.Marten.Specifications;

internal sealed class TaskCommentsSpecification(Guid taskId) : IListSpecification<TaskComment>
{
    public async Task<IReadOnlyList<TaskComment>> ApplyAsync(
        IQueryable<TaskComment> queryable,
        CancellationToken cancellationToken = default) =>
        await queryable
            .Where(comment => comment.TaskId == taskId)
            .OrderBy(comment => comment.CreatedAt)
            .ToListAsync(cancellationToken);
}

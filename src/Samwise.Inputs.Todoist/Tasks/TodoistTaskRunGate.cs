using Samwise.Domain.Tasks;
using Samwise.Logic.Tasks;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Inputs.Todoist.Tasks;

internal sealed class TodoistTaskRunGate(ITodoistTaskSyncService sync, IReadOnlyRepository<AgentTask> tasks) : IExternalTaskRunGate
{
    public string Provider => "Todoist";
    public async Task<bool> RefreshBeforeRunAsync(string taskId, CancellationToken cancellationToken)
    {
        var task = await tasks.GetRequiredByIdAsync(taskId, cancellationToken);
        var result = await sync.SynchronizeAsync(task.ExternalSource!.TaskId, cancellationToken);
        return result.Errors.Count == 0;
    }
}

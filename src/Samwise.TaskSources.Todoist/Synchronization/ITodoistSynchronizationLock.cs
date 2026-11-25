namespace Samwise.TaskSources.Todoist.Synchronization;

public interface ITodoistSynchronizationLock
{
    Task<IDisposable> EnterAsync(CancellationToken cancellationToken);
}

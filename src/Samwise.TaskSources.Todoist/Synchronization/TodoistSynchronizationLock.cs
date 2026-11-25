namespace Samwise.TaskSources.Todoist.Synchronization;

internal sealed class TodoistSynchronizationLock : ITodoistSynchronizationLock
{
    private readonly SemaphoreSlim semaphore = new(1, 1);

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}

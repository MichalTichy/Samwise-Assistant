namespace Samwise.Inputs.Todoist.Ingestion;

internal sealed class TodoistInputProcessingLock : ITodoistInputProcessingLock
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

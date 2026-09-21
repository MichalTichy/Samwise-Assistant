namespace Samwise.Inputs.Todoist.Ingestion;

public interface ITodoistInputProcessingLock
{
    Task<IDisposable> EnterAsync(CancellationToken cancellationToken);
}

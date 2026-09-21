namespace Samwise.Inputs.Teams.Ingestion;

public interface ITeamsInputProcessingLock
{
    Task<IDisposable> EnterAsync(CancellationToken cancellationToken);
}

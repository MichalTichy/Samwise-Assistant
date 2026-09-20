namespace Samwise.Inputs.Teams.Services;

public interface ITeamsInputProcessingLock
{
    Task<IDisposable> EnterAsync(CancellationToken cancellationToken);
}

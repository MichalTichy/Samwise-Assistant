namespace Samwise.Logic.Tasks;

public interface IExternalTaskRunGate
{
    string Provider { get; }
    Task<bool> RefreshBeforeRunAsync(string taskId, CancellationToken cancellationToken);
}

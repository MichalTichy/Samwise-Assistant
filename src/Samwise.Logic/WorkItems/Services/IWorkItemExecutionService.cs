namespace Samwise.Logic.WorkItems.Services;

internal interface IWorkItemExecutionService
{
    Task ExecuteAsync(WorkItemExecutionRequest request, CancellationToken cancellationToken);
}

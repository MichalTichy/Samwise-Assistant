namespace Samwise.Logic.WorkItems.Services;

internal interface IWorkItemPlanningService
{
    Task PlanAsync(string workItemId, CancellationToken cancellationToken);
}

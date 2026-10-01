namespace Samwise.Logic.WorkItems.Services;

internal interface IWorkItemAutomaticApprovalService
{
    Task AssessAsync(string workItemId, string planId, int workItemRevision, CancellationToken cancellationToken);
}

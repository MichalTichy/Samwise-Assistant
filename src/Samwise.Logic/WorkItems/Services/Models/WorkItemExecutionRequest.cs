namespace Samwise.Logic.WorkItems.Services;

internal sealed record WorkItemExecutionRequest(string WorkItemId, string PlanId, int WorkItemRevision);

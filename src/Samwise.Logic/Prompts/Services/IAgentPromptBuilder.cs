namespace Samwise.Logic.Prompts.Services;

internal interface IAgentPromptBuilder
{
    Task<IntakeAgentPrompt> BuildIntakeAsync(
        string inboundEventId,
        CancellationToken cancellationToken);

    Task<PlanningAgentPrompt> BuildPlanningAsync(
        string workItemId,
        CancellationToken cancellationToken);

    Task<ExecutionAgentPrompt> BuildExecutionAsync(
        string workItemId,
        int workItemRevision,
        string planId,
        CancellationToken cancellationToken);
}

namespace Samwise.Logic.Prompts.Services;

internal interface IAgentPromptBuilder
{
    Task<IntakeAgentPrompt> BuildIntakeAsync(
        string inboundEventId,
        CancellationToken cancellationToken);

    Task<PlanningAgentPrompt> BuildPlanningAsync(
        string workItemId,
        CancellationToken cancellationToken);

    Task<AutomaticApprovalAgentPrompt> BuildAutomaticApprovalAsync(
        string workItemId,
        int workItemRevision,
        string planId,
        CancellationToken cancellationToken);

    Task<ExecutionAgentPrompt> BuildExecutionAsync(
        string workItemId,
        int workItemRevision,
        string planId,
        CancellationToken cancellationToken,
        Samwise.Domain.Agents.AgentStage stage = Samwise.Domain.Agents.AgentStage.Execution);
}

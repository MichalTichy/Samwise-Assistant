using Microsoft.Extensions.Logging;
using Samwise.Domain.Agents;
using Samwise.Domain.WorkItems;
using Samwise.Logic.Agents;
using Samwise.Logic.Agents.Services;
using Samwise.Logic.Prompts.Services;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;

namespace Samwise.Logic.WorkItems.Services;

internal sealed class WorkItemAutomaticApprovalService(
    IRepository<DomainWorkItem> workItemRepository,
    IAgentRuntime runtime,
    IAgentToolCatalog toolCatalog,
    IAgentPromptBuilder promptBuilder,
    IAgentInvocationService invocationService,
    IWorkItemExecutionService executionService,
    IWorkItemEventPublisher eventPublisher,
    TimeProvider timeProvider,
    ILogger<WorkItemAutomaticApprovalService> logger) : IWorkItemAutomaticApprovalService
{
    public async Task AssessAsync(
        string workItemId,
        string planId,
        int workItemRevision,
        CancellationToken cancellationToken)
    {
        var workItem = await workItemRepository.GetRequiredByIdAsync(workItemId, cancellationToken);
        if (!IsCurrentUnreviewedPlan(workItem, planId, workItemRevision))
        {
            return;
        }

        var plan = workItem.FindCurrentPlan()!;
        if (plan.Playbook is not { SuitableForAutomaticApproval: true })
        {
            await RecordAsync(
                "The selected playbook is not marked suitable for automatic approval, or no playbook was selected.",
                false,
                null,
                null);
            return;
        }

        AgentInvocation? invocation = null;
        var invocationCompleted = false;
        string? catalogVersion = null;
        var startExecution = false;
        try
        {
            var approvalTools = await toolCatalog.GetToolsAsync(AgentStage.AutomaticApproval, cancellationToken);
            var prompt = await promptBuilder.BuildAutomaticApprovalAsync(
                workItemId, workItemRevision, planId, cancellationToken);
            catalogVersion = prompt.ExecutionTools.CatalogVersion;
            if (string.IsNullOrWhiteSpace(prompt.Configuration.Model) ||
                string.IsNullOrWhiteSpace(prompt.Configuration.ReasoningEffort) ||
                string.IsNullOrWhiteSpace(prompt.ExecutionConfiguration.Model) ||
                string.IsNullOrWhiteSpace(prompt.ExecutionConfiguration.ReasoningEffort))
            {
                await RecordAsync("Configure the automatic approval and restricted execution models before enabling this playbook.",
                    false, null, catalogVersion);
                return;
            }

            if (plan.Steps.Any(step => step.HasExternalSideEffects && step.ToolCalls.Count == 0))
            {
                await RecordAsync("A plan step has external effects but no explicit tool call to review.",
                    false, null, catalogVersion);
                return;
            }

            var unavailableTool = PlanExecutionToolCompatibility.FindIncompatibleTool(plan, prompt.ExecutionTools);
            if (unavailableTool is not null)
            {
                await RecordAsync(
                    $"The restricted execution profile cannot invoke planned tool '{unavailableTool}' with its approved definition.",
                    false, null, catalogVersion);
                return;
            }

            invocation = await invocationService.StartAsync(
                new AgentInvocationStart(
                    AgentStage.AutomaticApproval, prompt.Text, null, workItemId, workItemRevision,
                    prompt.Configuration, approvalTools),
                cancellationToken);
            var result = await runtime.AssessAutomaticApprovalAsync(
                new AutomaticApprovalRequest(prompt.Text, prompt.Configuration), cancellationToken);
            if (string.IsNullOrWhiteSpace(result.Reason))
            {
                throw new InvalidOperationException("The automatic approval agent did not explain its decision.");
            }

            await invocationService.CompleteAsync(invocation.Id, result.Receipt, cancellationToken);
            invocationCompleted = true;
            await RecordAsync(result.Reason.Trim(), result.Approve, invocation.Id, catalogVersion);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (invocation is not null && !invocationCompleted)
            {
                await invocationService.FailAsync(invocation.Id, AgentInvocationStatus.Failed,
                    "Automatic approval was cancelled.", CancellationToken.None);
            }

            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Automatic approval of plan {PlanId} failed", planId);
            if (invocation is not null && !invocationCompleted)
            {
                await invocationService.FailAsync(invocation.Id, AgentInvocationStatus.Failed,
                    exception.Message, CancellationToken.None);
            }

            await RecordAsync("Automatic approval could not be completed. Review the plan yourself.",
                false, invocation?.Id, catalogVersion);
        }

        if (startExecution)
        {
            await executionService.ExecuteAsync(
                new WorkItemExecutionRequest(workItemId, planId, workItemRevision), cancellationToken);
        }

        async Task RecordAsync(string reason, bool approved, string? invocationId, string? executionCatalogVersion)
        {
            var current = await workItemRepository.GetRequiredByIdAsync(workItemId, cancellationToken);
            if (!IsCurrentUnreviewedPlan(current, planId, workItemRevision))
            {
                return;
            }

            current.RecordAutomaticApprovalAssessment(planId, workItemRevision, new AutomaticApprovalAssessment
            {
                Approved = approved,
                Reason = reason,
                InvocationId = invocationId,
                ExecutionCatalogVersion = approved ? executionCatalogVersion : null,
                AssessedAt = timeProvider.GetUtcNow()
            });
            await workItemRepository.UpdateAsync(current, cancellationToken);
            await eventPublisher.PublishAsync(current);
            startExecution = approved;
        }
    }

    private static bool IsCurrentUnreviewedPlan(DomainWorkItem workItem, string planId, int revision) =>
        workItem.Status == WorkItemStatus.AwaitingApproval &&
        workItem.Revision == revision &&
        workItem.CurrentPlanId == planId &&
        workItem.FindCurrentPlan() is { Review: null, AutomaticApproval: null };
}

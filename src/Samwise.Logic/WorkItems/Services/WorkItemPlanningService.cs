using Microsoft.Extensions.Logging;
using Samwise.Domain.Agents;
using Samwise.Domain.WorkItems;
using Samwise.Logic.Agents;
using Samwise.Logic.Agents.Services;
using Samwise.Logic.Prompts.Services;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;
using WorkItemStatus = Samwise.Domain.WorkItems.WorkItemStatus;

namespace Samwise.Logic.WorkItems.Services;

internal interface IWorkItemPlanningService
{
    Task PlanAsync(string workItemId, CancellationToken cancellationToken);
}

internal sealed class WorkItemPlanningService(
    IRepository<DomainWorkItem> workItemRepository,
    IAgentRuntime runtime,
    IAgentPromptBuilder promptBuilder,
    IAgentInvocationService invocationService,
    TimeProvider timeProvider,
    ILogger<WorkItemPlanningService> logger) : IWorkItemPlanningService
{
    public async Task PlanAsync(string workItemId, CancellationToken cancellationToken)
    {
        var workItem = await workItemRepository.GetRequiredByIdAsync(workItemId, cancellationToken);
        if (workItem.Status != WorkItemStatus.Planning)
        {
            return;
        }

        var revision = workItem.Revision;
        var planNumber = workItem.ReservePlanNumber(timeProvider.GetUtcNow());
        await workItemRepository.UpdateAsync(workItem, cancellationToken);

        AgentInvocation? invocation = null;
        try
        {
            var prompt = await promptBuilder.BuildPlanningAsync(workItem.Id, cancellationToken);

            invocation = await invocationService.StartAsync(
                new AgentInvocationStart(
                    "Planning",
                    prompt.Text,
                    null,
                    workItem.Id,
                    revision,
                    prompt.Configuration,
                    prompt.PlanningTools),
                cancellationToken);
            var result = await runtime.PlanAsync(
                new PlannerRequest(
                    workItem.Id,
                    revision,
                    planNumber,
                    prompt.Text,
                    prompt.PlanningTools,
                    prompt.ExecutionTools,
                    prompt.Configuration),
                cancellationToken);
            AgentWorkflowSupport.ValidatePlan(result.Plan, prompt.ExecutionTools);
            var playbook = AgentWorkflowSupport.ResolvePlaybookSnapshot(
                result.Plan.PlaybookId,
                prompt.AvailablePlaybooks);
            await invocationService.CompleteAsync(invocation.Id, result.Receipt, cancellationToken);

            var currentWorkItem = await workItemRepository.GetRequiredByIdAsync(workItem.Id, cancellationToken);
            if (currentWorkItem.Revision != revision || currentWorkItem.Status != WorkItemStatus.Planning)
            {
                return;
            }

            var plan = new WorkItemPlan
            {
                Id = Guid.NewGuid().ToString(),
                WorkItemId = currentWorkItem.Id,
                WorkItemRevision = currentWorkItem.Revision,
                PlanNumber = planNumber,
                Summary = result.Plan.Summary,
                Assumptions = [.. result.Plan.Assumptions],
                Risks = [.. result.Plan.Risks],
                CompletionMode = result.Plan.CompletionMode,
                ExpectedFollowUp = result.Plan.ExpectedFollowUp,
                Playbook = playbook,
                Steps = result.Plan.Steps.Select(step => new PlanStep
                {
                    Order = step.Order,
                    Title = step.Title,
                    TechnicalDescription = step.TechnicalDescription,
                    ToolIds = [.. step.ToolIds],
                    HasExternalSideEffects = step.HasExternalSideEffects,
                    SuccessCriteria = step.SuccessCriteria
                }).ToList(),
                PlanningTools = AgentWorkflowSupport.CreatePlanToolSnapshot(prompt.PlanningTools),
                ExecutionTools = AgentWorkflowSupport.CreatePlanToolSnapshot(prompt.ExecutionTools),
                AgentConfigurationId = prompt.AgentConfigurationId,
                AgentConfigurationUpdatedAt = prompt.AgentConfigurationUpdatedAt,
                PlanningInvocationId = invocation.Id,
                CreatedAt = timeProvider.GetUtcNow()
            };
            currentWorkItem.ApplyPlan(plan, timeProvider.GetUtcNow());
            await workItemRepository.UpdateAsync(currentWorkItem, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (invocation is not null)
            {
                await invocationService.FailAsync(
                    invocation.Id,
                    AgentInvocationStatus.Failed,
                    "Planning was cancelled.",
                    CancellationToken.None);
            }

            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Planning work item {WorkItemId} failed", workItem.Id);
            if (invocation is not null)
            {
                await invocationService.FailAsync(
                    invocation.Id,
                    AgentInvocationStatus.Failed,
                    exception.Message,
                    cancellationToken);
            }

            var currentWorkItem = await workItemRepository.GetByIdAsync(workItem.Id, cancellationToken);
            if (currentWorkItem?.Revision == revision)
            {
                currentWorkItem.MarkPlanningFailed(timeProvider.GetUtcNow());
                await workItemRepository.UpdateAsync(currentWorkItem, cancellationToken);
            }
        }
    }
}

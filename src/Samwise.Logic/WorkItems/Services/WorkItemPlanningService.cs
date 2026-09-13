using Microsoft.Extensions.Logging;
using Samwise.Domain.Agents;
using Samwise.Domain.Playbooks;
using Samwise.Domain.WorkItems;
using Samwise.Logic.Agents;
using Samwise.Logic.Agents.Services;
using Samwise.Logic.Prompts;
using Samwise.Logic.Playbooks.Specifications;
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
    IReadOnlyRepository<AgentConfiguration> agentConfigurationRepository,
    IReadOnlyRepository<Playbook> playbookRepository,
    IAgentRuntime runtime,
    IAgentToolCatalog toolCatalog,
    IPromptRenderer promptRenderer,
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
            var agentConfiguration = await agentConfigurationRepository.GetRequiredByIdAsync(
                AgentConfiguration.DocumentId,
                cancellationToken);
            var stageConfiguration = agentConfiguration.GetStage(AgentStage.Planning);
            var playbooks = await playbookRepository.ListAsync(
                new PlaybooksSpecification(enabledOnly: true), cancellationToken);
            var comments = workItem.Comments;
            var previousPlan = workItem.FindLatestPlan();
            var planningCatalogTask = toolCatalog.GetToolsAsync(AgentStage.Planning, cancellationToken);
            var executionCatalogTask = toolCatalog.GetToolsAsync(AgentStage.Execution, cancellationToken);
            await Task.WhenAll(planningCatalogTask, executionCatalogTask);
            var planningCatalog = await planningCatalogTask;
            var executionCatalog = await executionCatalogTask;
            var renderResult = promptRenderer.Render(
                AgentStage.Planning,
                stageConfiguration.Prompt,
                AgentWorkflowSupport.BuildPlanningValues(
                    workItem,
                    comments,
                    previousPlan,
                    playbooks,
                    executionCatalog,
                    timeProvider.GetUtcNow()));
            if (!renderResult.Succeeded)
            {
                throw new InvalidOperationException(string.Join(" ", renderResult.Errors));
            }

            invocation = await invocationService.StartAsync(
                new AgentInvocationStart(
                    "Planning",
                    renderResult.Text,
                    null,
                    workItem.Id,
                    revision,
                    new AgentRunConfiguration(stageConfiguration.Model, stageConfiguration.ReasoningEffort),
                    planningCatalog),
                cancellationToken);
            var result = await runtime.PlanAsync(
                new PlannerRequest(
                    workItem.Id,
                    revision,
                    planNumber,
                    renderResult.Text,
                    planningCatalog,
                    executionCatalog,
                    new AgentRunConfiguration(stageConfiguration.Model, stageConfiguration.ReasoningEffort)),
                cancellationToken);
            AgentWorkflowSupport.ValidatePlan(result.Plan, executionCatalog);
            var playbook = AgentWorkflowSupport.ResolvePlaybookSnapshot(result.Plan.PlaybookId, playbooks);
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
                PlanningTools = AgentWorkflowSupport.CreatePlanToolSnapshot(planningCatalog),
                ExecutionTools = AgentWorkflowSupport.CreatePlanToolSnapshot(executionCatalog),
                AgentConfigurationId = agentConfiguration.Id,
                AgentConfigurationUpdatedAt = stageConfiguration.UpdatedAt,
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

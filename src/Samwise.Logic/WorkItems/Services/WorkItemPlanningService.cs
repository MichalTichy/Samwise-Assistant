using Microsoft.Extensions.Logging;
using Samwise.Domain.Agents;
using Samwise.Domain.Prompts;
using Samwise.Domain.WorkItems;
using Samwise.Logic.Agents;
using Samwise.Logic.Agents.Services;
using Samwise.Logic.Prompts;
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
    IReadOnlyRepository<PromptTemplate> promptRepository,
    IAgentRuntime runtime,
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
            var template = await promptRepository.GetRequiredByIdAsync("planning", cancellationToken);
            var comments = workItem.Comments;
            var previousPlan = workItem.FindLatestPlan();
            var planningCatalog = await runtime.GetPlanningToolsAsync(cancellationToken);
            var executionCatalog = await runtime.GetExecutionToolsAsync(cancellationToken);
            var renderResult = promptRenderer.Render(
                PromptStage.Planning,
                template.Content,
                AgentWorkflowSupport.BuildPlanningValues(
                    workItem,
                    comments,
                    previousPlan,
                    executionCatalog,
                    timeProvider.GetUtcNow()));
            if (!renderResult.Succeeded)
            {
                throw new InvalidOperationException(string.Join(" ", renderResult.Errors));
            }

            invocation = await invocationService.StartAsync(
                new AgentInvocationStart("Planning", renderResult.Text, null, workItem.Id, revision),
                cancellationToken);
            var result = await runtime.PlanAsync(
                new PlannerRequest(
                    workItem.Id,
                    revision,
                    planNumber,
                    renderResult.Text,
                    planningCatalog,
                    executionCatalog),
                cancellationToken);
            AgentWorkflowSupport.ValidatePlan(result.Plan, executionCatalog);
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
                PromptTemplateId = template.Id,
                PromptTemplateUpdatedAt = template.UpdatedAt,
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

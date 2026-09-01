using Microsoft.Extensions.Logging;
using Samwise.Domain.Agents;
using Samwise.Logic.Agents;
using Samwise.Logic.Agents.Services;
using Samwise.Logic.Prompts;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;
using WorkItemStatus = Samwise.Domain.WorkItems.WorkItemStatus;

namespace Samwise.Logic.WorkItems.Services;

internal sealed record WorkItemExecutionRequest(string WorkItemId, string PlanId, int WorkItemRevision);

internal interface IWorkItemExecutionService
{
    Task ExecuteAsync(WorkItemExecutionRequest request, CancellationToken cancellationToken);
}

internal sealed class WorkItemExecutionService(
    IRepository<DomainWorkItem> workItemRepository,
    IReadOnlyRepository<AgentConfiguration> agentConfigurationRepository,
    IAgentRuntime runtime,
    IPromptRenderer promptRenderer,
    IAgentInvocationService invocationService,
    TimeProvider timeProvider,
    ILogger<WorkItemExecutionService> logger) : IWorkItemExecutionService
{
    public async Task ExecuteAsync(WorkItemExecutionRequest request, CancellationToken cancellationToken)
    {
        var workItem = await workItemRepository.GetRequiredByIdAsync(request.WorkItemId, cancellationToken);
        if (workItem.Status != WorkItemStatus.Approved ||
            workItem.Revision != request.WorkItemRevision ||
            workItem.CurrentPlanId != request.PlanId)
        {
            return;
        }

        AgentInvocation? invocation = null;
        var executionStarted = false;
        try
        {
            var plan = workItem.FindCurrentPlan()
                ?? throw new InvalidOperationException("The approved plan is missing from the work item document.");
            var agentConfiguration = await agentConfigurationRepository.GetRequiredByIdAsync(
                AgentConfiguration.DocumentId,
                cancellationToken);
            var stageConfiguration = agentConfiguration.GetStage(AgentStage.Execution);
            var comments = workItem.Comments;
            var catalog = await runtime.GetExecutionToolsAsync(cancellationToken);
            if (!string.Equals(plan.ExecutionTools.CatalogVersion, catalog.CatalogVersion, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The execution tool catalog changed after approval. Replanning is required.");
            }

            var rendered = promptRenderer.Render(
                AgentStage.Execution,
                stageConfiguration.Prompt,
                AgentWorkflowSupport.BuildExecutionValues(
                    workItem,
                    comments,
                    plan,
                    catalog,
                    timeProvider.GetUtcNow()));
            if (!rendered.Succeeded)
            {
                throw new InvalidOperationException(string.Join(" ", rendered.Errors));
            }

            invocation = await invocationService.StartAsync(
                new AgentInvocationStart(
                    "Execution",
                    rendered.Text,
                    null,
                    workItem.Id,
                    workItem.Revision,
                    new AgentRunConfiguration(stageConfiguration.Model, stageConfiguration.ReasoningEffort)),
                cancellationToken);
            workItem.StartExecution(plan.Id, request.WorkItemRevision, timeProvider.GetUtcNow());
            await workItemRepository.UpdateAsync(workItem, cancellationToken);
            executionStarted = true;

            var result = await runtime.ExecuteAsync(
                new ExecutionRequest(
                    workItem.Id,
                    workItem.Revision,
                    plan.Id,
                    rendered.Text,
                    catalog,
                    new AgentRunConfiguration(stageConfiguration.Model, stageConfiguration.ReasoningEffort)),
                cancellationToken);
            await invocationService.CompleteAsync(invocation.Id, result.Receipt, cancellationToken);

            var currentWorkItem = await workItemRepository.GetRequiredByIdAsync(workItem.Id, cancellationToken);
            if (result.Succeeded)
            {
                currentWorkItem.CompleteExecution(timeProvider.GetUtcNow());
            }
            else
            {
                currentWorkItem.MarkExecutionUncertain(timeProvider.GetUtcNow());
            }

            currentWorkItem.AddExecutionUpdate(
                AgentWorkflowSupport.FormatExecutionResult(result),
                timeProvider.GetUtcNow());
            await workItemRepository.UpdateAsync(currentWorkItem, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RecordFailureAsync(new OperationCanceledException("Execution was cancelled."), executionStarted);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Executing work item {WorkItemId} failed", workItem.Id);
            await RecordFailureAsync(exception, executionStarted);
        }

        async Task RecordFailureAsync(Exception exception, bool mayHaveSideEffects)
        {
            if (invocation is not null)
            {
                await invocationService.FailAsync(
                    invocation.Id,
                    mayHaveSideEffects ? AgentInvocationStatus.OutcomeUnknown : AgentInvocationStatus.Failed,
                    exception.Message,
                    CancellationToken.None);
            }

            var currentWorkItem = await workItemRepository.GetByIdAsync(workItem.Id, CancellationToken.None);
            if (currentWorkItem is null)
            {
                return;
            }

            if (mayHaveSideEffects)
            {
                currentWorkItem.MarkExecutionUncertain(timeProvider.GetUtcNow());
            }
            else
            {
                currentWorkItem.MarkApprovedExecutionFailed(timeProvider.GetUtcNow());
            }

            await workItemRepository.UpdateAsync(currentWorkItem, CancellationToken.None);
        }
    }
}

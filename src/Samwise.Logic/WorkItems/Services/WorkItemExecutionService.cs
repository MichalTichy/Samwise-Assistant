using Microsoft.Extensions.Logging;
using Samwise.Domain.Agents;
using Samwise.Logic.Agents;
using Samwise.Logic.Agents.Services;
using Samwise.Logic.Prompts.Services;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;
using WorkItemStatus = Samwise.Domain.WorkItems.WorkItemStatus;

namespace Samwise.Logic.WorkItems.Services;

internal sealed class WorkItemExecutionService(
    IRepository<DomainWorkItem> workItemRepository,
    IAgentRuntime runtime,
    IAgentPromptBuilder promptBuilder,
    IAgentInvocationService invocationService,
    IWorkItemEventPublisher eventPublisher,
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
            var stage = plan.Review?.Source == DomainWorkItem.AutomaticApprovalSource
                ? AgentStage.AutoApprovedExecution
                : AgentStage.Execution;
            var approvedCatalogVersion = stage == AgentStage.AutoApprovedExecution
                ? plan.AutomaticApproval is { Approved: true } assessment
                    ? assessment.ExecutionCatalogVersion
                    : throw new InvalidOperationException("The automatic approval assessment is missing.")
                : plan.ExecutionTools.CatalogVersion;
            var prompt = await promptBuilder.BuildExecutionAsync(
                workItem.Id,
                request.WorkItemRevision,
                plan.Id,
                cancellationToken,
                stage);
            if (!string.Equals(
                    approvedCatalogVersion,
                    prompt.ExecutionTools.CatalogVersion,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The execution tool catalog changed after approval. Replanning is required.");
            }

            invocation = await invocationService.StartAsync(
                new AgentInvocationStart(
                    stage == AgentStage.AutoApprovedExecution ? "Auto-approved execution" : "Execution",
                    prompt.Text,
                    null,
                    workItem.Id,
                    workItem.Revision,
                    prompt.Configuration,
                    prompt.ExecutionTools),
                cancellationToken);
            workItem.StartExecution(plan.Id, request.WorkItemRevision, timeProvider.GetUtcNow());
            await workItemRepository.UpdateAsync(workItem, cancellationToken);
            await eventPublisher.PublishAsync(workItem);
            executionStarted = true;

            var result = await runtime.ExecuteAsync(
                new ExecutionRequest(
                    workItem.Id,
                    workItem.Revision,
                    plan.Id,
                    prompt.Text,
                    prompt.ExecutionTools,
                    prompt.Configuration,
                    stage),
                cancellationToken);
            await invocationService.CompleteAsync(invocation.Id, result.Receipt, cancellationToken);

            var currentWorkItem = await workItemRepository.GetRequiredByIdAsync(workItem.Id, cancellationToken);
            switch (result.Disposition)
            {
                case ExecutionDisposition.Succeeded:
                    currentWorkItem.CompleteExecution(
                        result.Summary,
                        result.Outputs,
                        invocation.Id,
                        timeProvider.GetUtcNow());
                    break;
                case ExecutionDisposition.Failed:
                    currentWorkItem.MarkExecutionFailed(timeProvider.GetUtcNow(), result.Summary);
                    currentWorkItem.AddExecutionUpdate(
                        AgentWorkflowSupport.FormatExecutionResult(result),
                        timeProvider.GetUtcNow());
                    break;
                case ExecutionDisposition.OutcomeUnknown:
                    currentWorkItem.MarkExecutionUncertain(timeProvider.GetUtcNow(), result.Summary);
                    currentWorkItem.AddExecutionUpdate(
                        AgentWorkflowSupport.FormatExecutionResult(result),
                        timeProvider.GetUtcNow());
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(result.Disposition),
                        result.Disposition,
                        "Unknown execution disposition.");
            }

            await workItemRepository.UpdateAsync(currentWorkItem, cancellationToken);
            await eventPublisher.PublishAsync(currentWorkItem);
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
                currentWorkItem.MarkExecutionUncertain(timeProvider.GetUtcNow(), exception.Message);
            }
            else
            {
                currentWorkItem.MarkExecutionFailed(timeProvider.GetUtcNow(), exception.Message);
            }

            await workItemRepository.UpdateAsync(currentWorkItem, CancellationToken.None);
            await eventPublisher.PublishAsync(currentWorkItem);
        }
    }
}

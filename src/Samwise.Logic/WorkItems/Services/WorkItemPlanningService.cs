using Microsoft.Extensions.Logging;
using Samwise.Domain.Agents;
using Samwise.Domain.WorkItems;
using Samwise.Logic.Agents;
using Samwise.Logic.Agents.Services;
using Samwise.Logic.Notifications.Services;
using Samwise.Logic.Prompts.Services;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;
using WorkItemStatus = Samwise.Domain.WorkItems.WorkItemStatus;

namespace Samwise.Logic.WorkItems.Services;

internal sealed class WorkItemPlanningService(
    IRepository<DomainWorkItem> workItemRepository,
    IAgentRuntime runtime,
    IAgentPromptBuilder promptBuilder,
    IAgentInvocationService invocationService,
    IWorkItemAttentionNotifier attentionNotifier,
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
            PlanPlaybookSnapshot? playbook = null;
            if (result.Disposition == PlanningDisposition.PlanRequired)
            {
                if (result.ResolutionReason is not null || result.Question is not null)
                {
                    throw new InvalidOperationException(
                        "A plan-required result cannot include a resolution reason or user question.");
                }

                AgentWorkflowSupport.ValidatePlan(result.Plan, prompt.ExecutionTools);
                playbook = AgentWorkflowSupport.ResolvePlaybookSnapshot(
                    result.Plan.PlaybookId,
                    prompt.AvailablePlaybooks);
            }
            else if (result.Disposition == PlanningDisposition.UserInputRequired)
            {
                if (result.ResolutionReason is not null)
                {
                    throw new InvalidOperationException(
                        "A user-input-required result cannot include a resolution reason.");
                }

                ValidateEmptyPlan(result);
                AgentWorkflowSupport.ValidatePlanningQuestion(result.Question);
            }
            else if (string.IsNullOrWhiteSpace(result.ResolutionReason))
            {
                throw new InvalidOperationException(
                    $"Planning disposition '{result.Disposition}' requires a resolution reason.");
            }
            else if (result.Question is not null)
            {
                throw new InvalidOperationException(
                    $"Planning disposition '{result.Disposition}' cannot include a user question.");
            }
            else
            {
                ValidateEmptyPlan(result);
            }

            await invocationService.CompleteAsync(invocation.Id, result.Receipt, cancellationToken);

            var currentWorkItem = await workItemRepository.GetRequiredByIdAsync(workItem.Id, cancellationToken);
            if (currentWorkItem.Revision != revision || currentWorkItem.Status != WorkItemStatus.Planning)
            {
                return;
            }

            if (result.Disposition == PlanningDisposition.AlreadyResolved)
            {
                currentWorkItem.CompleteDuringPlanning(
                    result.ResolutionReason!,
                    invocation.Id,
                    timeProvider.GetUtcNow());
                await workItemRepository.UpdateAsync(currentWorkItem, cancellationToken);
                return;
            }

            if (result.Disposition == PlanningDisposition.NoLongerRelevant)
            {
                currentWorkItem.DiscardDuringPlanning(
                    result.ResolutionReason!,
                    invocation.Id,
                    timeProvider.GetUtcNow());
                await workItemRepository.UpdateAsync(currentWorkItem, cancellationToken);
                return;
            }

            if (result.Disposition == PlanningDisposition.UserInputRequired)
            {
                var question = result.Question!;
                currentWorkItem.RequestUserInput(new PlanningQuestion
                {
                    Id = Guid.NewGuid().ToString(),
                    WorkItemRevision = currentWorkItem.Revision,
                    PlanningAttemptNumber = planNumber,
                    Prompt = question.Prompt.Trim(),
                    Explanation = string.IsNullOrWhiteSpace(question.Explanation)
                        ? null
                        : question.Explanation.Trim(),
                    Options = question.Options.Select(option => new PlanningQuestionOption
                    {
                        Id = Guid.NewGuid().ToString(),
                        Label = option.Label.Trim(),
                        Description = string.IsNullOrWhiteSpace(option.Description)
                            ? null
                            : option.Description.Trim()
                    }).ToList(),
                    PlanningInvocationId = invocation.Id,
                    CreatedAt = timeProvider.GetUtcNow()
                }, timeProvider.GetUtcNow());
                await workItemRepository.UpdateAsync(currentWorkItem, cancellationToken);
                await attentionNotifier.NotifyAsync(currentWorkItem, currentWorkItem.PendingPlanningQuestion?.Prompt);
                return;
            }

            if (result.Disposition != PlanningDisposition.PlanRequired)
            {
                throw new InvalidOperationException(
                    $"Unsupported planning disposition '{result.Disposition}'.");
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
                Decisions = currentWorkItem.PlanningDecisions.Select(decision => new PlanDecisionSnapshot
                {
                    Question = decision.Question,
                    Answer = decision.Answer,
                    AnsweredAt = decision.AnsweredAt
                }).ToList(),
                Playbook = playbook,
                Steps = result.Plan.Steps.Select(step => new PlanStep
                {
                    Order = step.Order,
                    Title = step.Title,
                    TechnicalDescription = step.TechnicalDescription,
                    ToolCalls = step.ToolCalls.Select(toolCall => new PlanToolCall
                    {
                        ToolId = toolCall.ToolId,
                        ArgumentsJson = toolCall.ArgumentsJson
                    }).ToList(),
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
            await attentionNotifier.NotifyAsync(currentWorkItem);
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
                await attentionNotifier.NotifyAsync(currentWorkItem);
            }
        }
    }

    private static void ValidateEmptyPlan(PlannerResult result)
    {
        if (result.Plan.PlaybookId is not null ||
            !string.IsNullOrWhiteSpace(result.Plan.Summary) ||
            result.Plan.Assumptions.Count > 0 ||
            result.Plan.Risks.Count > 0 ||
            result.Plan.Steps.Count > 0)
        {
            throw new InvalidOperationException(
                $"Planning disposition '{result.Disposition}' cannot include an executable plan.");
        }
    }
}

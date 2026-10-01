using Microsoft.Extensions.Logging;
using Samwise.Domain.Agents;
using Samwise.Domain.Inbox;
using Samwise.Domain.WorkItems;
using Samwise.Logic.Agents;
using Samwise.Logic.Agents.Services;
using Samwise.Logic.Prompts.Services;
using Shared.Infrastructure.Persistence.Marten.UnitOfWorks;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;
using WorkItemStatus = Samwise.Domain.WorkItems.WorkItemStatus;
using Samwise.Logic.WorkItems.Services;

namespace Samwise.Logic.Inbox.Services;

internal sealed class InboundEventClassificationService(
    IRepository<InboundEvent> inboundRepository,
    IRepository<DomainWorkItem> workItemRepository,
    IAgentRuntime runtime,
    IAgentPromptBuilder promptBuilder,
    IAgentInvocationService invocationService,
    IWorkItemEventPublisher eventPublisher,
    TimeProvider timeProvider,
    ILogger<InboundEventClassificationService> logger) : IInboundEventClassificationService
{
    public async Task<IReadOnlyList<string>> ClassifyAsync(
        string inboundEventId,
        CancellationToken cancellationToken)
    {
        var inboundEvent = await inboundRepository.GetRequiredByIdAsync(inboundEventId, cancellationToken);
        if (inboundEvent.Status != InboundEventStatus.Pending)
        {
            return [];
        }

        AgentInvocation? invocation = null;
        try
        {
            var prompt = await promptBuilder.BuildIntakeAsync(inboundEvent.Id, cancellationToken);

            invocation = await invocationService.StartAsync(
                new AgentInvocationStart(
                    AgentStage.Intake,
                    prompt.Text,
                    inboundEvent.Id,
                    null,
                    0,
                    prompt.Configuration,
                    prompt.IntakeTools),
                cancellationToken);
            var result = await runtime.ClassifyAsync(
                new IntakeRequest(
                    inboundEvent.Id,
                    inboundEvent.Source,
                    prompt.Text,
                    prompt.Configuration),
                cancellationToken);
            await invocationService.CompleteAsync(invocation.Id, result.Receipt, cancellationToken);
            AgentWorkflowSupport.ValidateIntakeResult(result);

            inboundEvent.HandledAt = timeProvider.GetUtcNow();
            inboundEvent.Explanation = string.IsNullOrWhiteSpace(result.Explanation)
                ? "Intake completed without an explanation."
                : result.Explanation.Trim();

            var workItemsToPlan = new List<DomainWorkItem>();
            var appendTargets = await ResolveAppendTargetsAsync(result, prompt.ActiveWorkItems, cancellationToken);
            if (appendTargets is null)
            {
                MarkForReview(
                    inboundEvent,
                    "One or more selected work items are missing or are not active candidates.");
                await inboundRepository.UpdateAsync(inboundEvent, cancellationToken);
                return [];
            }

            await using (var unitOfWork = new UnitOfWorkProvider())
            {
                switch (result.Decision)
                {
                    case IntakeDecision.ApplyChanges:
                        foreach (var change in result.WorkItems)
                        {
                            var workItem = change.Action switch
                            {
                                IntakeWorkItemAction.Create => DomainWorkItem.CreateFromInbound(
                                    change.RelevantText,
                                    change.ProposedTitle,
                                    timeProvider.GetUtcNow(),
                                    CreateSourceReference(inboundEvent)),
                                IntakeWorkItemAction.Append => appendTargets[change.ExistingWorkItemId!.Trim()],
                                _ => throw new InvalidOperationException(
                                    $"Unsupported intake work item action '{change.Action}'.")
                            };

                            if (change.Action == IntakeWorkItemAction.Create)
                            {
                                await workItemRepository.AddAsync(workItem, cancellationToken);
                            }
                            else
                            {
                                workItem.AddInboundContext(
                                    change.RelevantText,
                                    CreateSourceReference(inboundEvent),
                                    timeProvider.GetUtcNow());
                                await workItemRepository.UpdateAsync(workItem, cancellationToken);
                            }

                            inboundEvent.Outcomes.Add(new InboundEventOutcome
                            {
                                Id = Guid.NewGuid().ToString(),
                                Kind = change.Action == IntakeWorkItemAction.Create
                                    ? InboundEventOutcomeKind.CreatedWorkItem
                                    : InboundEventOutcomeKind.UpdatedWorkItem,
                                WorkItemId = workItem.Id,
                                RelevantText = change.RelevantText.Trim(),
                                Explanation = change.Explanation.Trim()
                            });
                            workItemsToPlan.Add(workItem);
                        }

                        inboundEvent.Status = InboundEventStatus.Processed;
                        break;
                    case IntakeDecision.Ignore:
                        inboundEvent.Status = InboundEventStatus.Processed;
                        break;
                    case IntakeDecision.NeedsReview:
                        inboundEvent.Status = InboundEventStatus.NeedsReview;
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported intake decision '{result.Decision}'.");
                }

                await inboundRepository.UpdateAsync(inboundEvent, cancellationToken);
                await unitOfWork.CommitAsync();
            }

            foreach (var workItem in workItemsToPlan)
            {
                await eventPublisher.PublishAsync(workItem);
            }

            return workItemsToPlan.Select(workItem => workItem.Id).ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Classifying inbound event {InboundEventId} failed", inboundEvent.Id);
            if (invocation is not null)
            {
                await invocationService.FailAsync(
                    invocation.Id,
                    AgentInvocationStatus.Failed,
                    exception.Message,
                    cancellationToken);
            }

            inboundEvent.Status = InboundEventStatus.NeedsReview;
            inboundEvent.Outcomes = [];
            inboundEvent.HandledAt = timeProvider.GetUtcNow();
            inboundEvent.Explanation = $"Intake classification failed: {exception.Message}";
            await inboundRepository.UpdateAsync(inboundEvent, cancellationToken);
            return [];
        }
    }

    private async Task<IReadOnlyDictionary<string, DomainWorkItem>?> ResolveAppendTargetsAsync(
        IntakeResult result,
        IReadOnlyList<DomainWorkItem> candidates,
        CancellationToken cancellationToken)
    {
        var candidateIds = candidates.Select(workItem => workItem.Id).ToHashSet(StringComparer.Ordinal);
        var targetIds = result.WorkItems
            .Where(change => change.Action == IntakeWorkItemAction.Append)
            .Select(change => change.ExistingWorkItemId!.Trim())
            .ToList();
        if (targetIds.Any(id => !candidateIds.Contains(id)))
        {
            return null;
        }

        var targets = new Dictionary<string, DomainWorkItem>(StringComparer.Ordinal);
        foreach (var targetId in targetIds)
        {
            var workItem = await workItemRepository.GetByIdAsync(targetId, cancellationToken);
            if (workItem?.Status is null or
                WorkItemStatus.Completed or
                WorkItemStatus.Discarded or
                WorkItemStatus.Executing or
                WorkItemStatus.OutcomeUnknown)
            {
                return null;
            }

            targets.Add(targetId, workItem);
        }

        return targets;
    }

    private static void MarkForReview(InboundEvent inboundEvent, string reason)
    {
        inboundEvent.Status = InboundEventStatus.NeedsReview;
        inboundEvent.Outcomes = [];
        inboundEvent.Explanation = reason;
    }

    private static WorkItemSourceReference CreateSourceReference(InboundEvent inboundEvent) => new()
        {
            InboundEventId = inboundEvent.Id,
            Source = inboundEvent.Source,
            ExternalId = inboundEvent.ExternalId,
            AddedAt = inboundEvent.ReceivedAt
        };
}

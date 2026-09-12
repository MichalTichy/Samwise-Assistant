using Microsoft.Extensions.Logging;
using Samwise.Domain.Agents;
using Samwise.Domain.Inbox;
using Samwise.Domain.WorkItems;
using Samwise.Logic.Agents;
using Samwise.Logic.Agents.Services;
using Samwise.Logic.Prompts;
using Samwise.Logic.WorkItems.Specifications;
using Shared.Infrastructure.Persistence.Marten.UnitOfWorks;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;
using WorkItemStatus = Samwise.Domain.WorkItems.WorkItemStatus;

namespace Samwise.Logic.Inbox.Services;

internal interface IInboundEventClassificationService
{
    Task<string?> ClassifyAsync(string inboundEventId, CancellationToken cancellationToken);
}

internal sealed class InboundEventClassificationService(
    IRepository<InboundEvent> inboundRepository,
    IRepository<DomainWorkItem> workItemRepository,
    IReadOnlyRepository<AgentConfiguration> agentConfigurationRepository,
    IAgentRuntime runtime,
    IPromptRenderer promptRenderer,
    IAgentInvocationService invocationService,
    TimeProvider timeProvider,
    ILogger<InboundEventClassificationService> logger) : IInboundEventClassificationService
{
    public async Task<string?> ClassifyAsync(string inboundEventId, CancellationToken cancellationToken)
    {
        var inboundEvent = await inboundRepository.GetRequiredByIdAsync(inboundEventId, cancellationToken);
        if (inboundEvent.Handling != InboundHandlingKind.Pending)
        {
            return null;
        }

        AgentInvocation? invocation = null;
        try
        {
            var agentConfiguration = await agentConfigurationRepository.GetRequiredByIdAsync(
                AgentConfiguration.DocumentId,
                cancellationToken);
            var stageConfiguration = agentConfiguration.GetStage(AgentStage.Intake);
            var candidates = await workItemRepository.ListAsync(
                new ActiveWorkItemCandidatesSpecification(), cancellationToken);
            var rendered = promptRenderer.Render(
                AgentStage.Intake,
                stageConfiguration.Prompt,
                AgentWorkflowSupport.BuildIntakeValues(inboundEvent, candidates, timeProvider.GetUtcNow()));
            if (!rendered.Succeeded)
            {
                throw new InvalidOperationException(string.Join(" ", rendered.Errors));
            }

            invocation = await invocationService.StartAsync(
                new AgentInvocationStart(
                    "Intake",
                    rendered.Text,
                    inboundEvent.Id,
                    null,
                    0,
                    new AgentRunConfiguration(stageConfiguration.Model, stageConfiguration.ReasoningEffort)),
                cancellationToken);
            var result = await runtime.ClassifyAsync(
                new IntakeRequest(
                    inboundEvent.Id,
                    inboundEvent.Source,
                    rendered.Text,
                    new AgentRunConfiguration(stageConfiguration.Model, stageConfiguration.ReasoningEffort)),
                cancellationToken);
            await invocationService.CompleteAsync(invocation.Id, result.Receipt, cancellationToken);

            inboundEvent.HandledAt = timeProvider.GetUtcNow();
            inboundEvent.Explanation = string.IsNullOrWhiteSpace(result.Explanation)
                ? "Intake completed without an explanation."
                : result.Explanation.Trim();

            DomainWorkItem? workItemToPlan = null;
            await using (var unitOfWork = new UnitOfWorkProvider())
            {
                switch (result.Decision)
                {
                    case IntakeDecision.CreateWorkItem:
                        workItemToPlan = DomainWorkItem.CreateFromInbound(
                            inboundEvent.GetIntakeText(), result.ProposedTitle, timeProvider.GetUtcNow());
                        await workItemRepository.AddAsync(workItemToPlan, cancellationToken);
                        inboundEvent.Handling = InboundHandlingKind.CreateWorkItem;
                        inboundEvent.WorkItemId = workItemToPlan.Id;
                        break;

                    case IntakeDecision.AppendToWorkItem:
                        workItemToPlan = await FindAppendTargetAsync(result, candidates, cancellationToken);
                        if (workItemToPlan is null)
                        {
                            MarkForReview(
                                inboundEvent,
                                "The selected work item is missing or is not an active candidate.");
                            break;
                        }

                        workItemToPlan.AddContext(inboundEvent.GetIntakeText(), timeProvider.GetUtcNow());
                        await workItemRepository.UpdateAsync(workItemToPlan, cancellationToken);
                        inboundEvent.Handling = InboundHandlingKind.AppendToWorkItem;
                        inboundEvent.WorkItemId = workItemToPlan.Id;
                        break;

                    case IntakeDecision.Ignore:
                        inboundEvent.Handling = InboundHandlingKind.Ignore;
                        break;
                    case IntakeDecision.NeedsReview:
                        inboundEvent.Handling = InboundHandlingKind.NeedsReview;
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported intake decision '{result.Decision}'.");
                }

                await inboundRepository.UpdateAsync(inboundEvent, cancellationToken);
                await unitOfWork.CommitAsync();
            }

            return workItemToPlan?.Id;
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

            inboundEvent.Handling = InboundHandlingKind.NeedsReview;
            inboundEvent.HandledAt = timeProvider.GetUtcNow();
            inboundEvent.Explanation = $"Intake classification failed: {exception.Message}";
            await inboundRepository.UpdateAsync(inboundEvent, cancellationToken);
            return null;
        }
    }

    private async Task<DomainWorkItem?> FindAppendTargetAsync(
        IntakeResult result,
        IReadOnlyList<DomainWorkItem> candidates,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(result.ExistingWorkItemId) ||
            !candidates.Any(workItem => workItem.Id == result.ExistingWorkItemId))
        {
            return null;
        }

        var workItem = await workItemRepository.GetByIdAsync(result.ExistingWorkItemId, cancellationToken);
        return workItem?.Status is WorkItemStatus.Completed or WorkItemStatus.Discarded ? null : workItem;
    }

    private static void MarkForReview(InboundEvent inboundEvent, string reason)
    {
        inboundEvent.Handling = InboundHandlingKind.NeedsReview;
        inboundEvent.Explanation = reason;
    }
}

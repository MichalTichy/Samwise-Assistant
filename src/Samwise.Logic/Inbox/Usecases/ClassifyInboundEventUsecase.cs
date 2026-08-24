using MediatR;
using Microsoft.Extensions.Logging;
using Samwise.Domain.Agents;
using Samwise.Domain.Inbox;
using Samwise.Domain.Prompts;
using Samwise.Domain.Tasks;
using Samwise.Logic.Agents;
using Samwise.Logic.Prompts;
using Samwise.Logic.Tasks.Specifications;
using Samwise.Logic.Tasks.Usecases;
using Shared.Infrastructure.Persistence.Repositories;
using DomainTask = Samwise.Domain.Tasks.Task;
using Task = System.Threading.Tasks.Task;

namespace Samwise.Logic.Inbox.Usecases;

internal sealed record ClassifyInboundEventUsecase(string InboundEventId) : IRequest;

internal sealed class ClassifyInboundEventUsecaseHandler(
    IRepository<InboundEvent> inboundRepository,
    IRepository<DomainTask> taskRepository,
    IRepository<TaskComment> commentRepository,
    IReadOnlyRepository<PromptTemplate> promptRepository,
    IAgentRuntime runtime,
    IPromptRenderer promptRenderer,
    AgentInvocationRecorder invocationRecorder,
    ISender sender,
    TimeProvider timeProvider,
    ILogger<ClassifyInboundEventUsecaseHandler> logger) : IRequestHandler<ClassifyInboundEventUsecase>
{
    public async Task Handle(ClassifyInboundEventUsecase request, CancellationToken cancellationToken)
    {
        var inboundEvent = await inboundRepository.GetRequiredByIdAsync(request.InboundEventId, cancellationToken);
        if (inboundEvent.Handling != InboundHandlingKind.Pending)
        {
            return;
        }

        AgentInvocation? invocation = null;
        try
        {
            var template = await promptRepository.GetRequiredByIdAsync("intake", cancellationToken);
            var candidates = await taskRepository.ListAsync(new ActiveTaskCandidatesSpecification(), cancellationToken);
            var rendered = promptRenderer.Render(
                PromptStage.Intake,
                template.Content,
                AgentWorkflowSupport.BuildIntakeValues(inboundEvent, candidates, timeProvider.GetUtcNow()));
            if (!rendered.Succeeded)
            {
                throw new InvalidOperationException(string.Join(" ", rendered.Errors));
            }

            invocation = await invocationRecorder.StartAsync(
                "Intake", rendered.Text, inboundEvent.Id, null, 0, cancellationToken);
            var result = await runtime.ClassifyAsync(
                new IntakeRequest(inboundEvent.Id, inboundEvent.Source, rendered.Text), cancellationToken);
            await invocationRecorder.SucceedAsync(invocation, result.Receipt, cancellationToken);

            inboundEvent.HandledAt = timeProvider.GetUtcNow();
            inboundEvent.Explanation = string.IsNullOrWhiteSpace(result.Explanation)
                ? "Intake completed without an explanation."
                : result.Explanation.Trim();

            DomainTask? taskToPlan = null;
            switch (result.Decision)
            {
                case IntakeDecision.CreateTask:
                    taskToPlan = DomainTask.CreateFromInbound(
                        inboundEvent.Text, result.ProposedTitle, timeProvider.GetUtcNow());
                    await taskRepository.AddAsync(taskToPlan, cancellationToken);
                    inboundEvent.Handling = InboundHandlingKind.CreateTask;
                    inboundEvent.TaskId = taskToPlan.Id;
                    break;

                case IntakeDecision.AppendToTask:
                    taskToPlan = await FindAppendTargetAsync(result, candidates, cancellationToken);
                    if (taskToPlan is null)
                    {
                        MarkForReview(inboundEvent, "The selected task is missing or is not an active candidate.");
                        break;
                    }

                    taskToPlan.AddContext(inboundEvent.Text, timeProvider.GetUtcNow());
                    await taskRepository.UpdateAsync(taskToPlan, cancellationToken);
                    await commentRepository.AddAsync(new TaskComment
                    {
                        Id = Guid.NewGuid().ToString(),
                        TaskId = taskToPlan.Id,
                        Body = inboundEvent.Text,
                        AuthorKind = CommentAuthorKind.User,
                        Kind = TaskCommentKind.Context,
                        CreatedAt = timeProvider.GetUtcNow()
                    }, cancellationToken);
                    inboundEvent.Handling = InboundHandlingKind.AppendToTask;
                    inboundEvent.TaskId = taskToPlan.Id;
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
            if (taskToPlan is not null)
            {
                await sender.Send(new PlanTaskUsecase(taskToPlan.Id), cancellationToken);
            }
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
                await invocationRecorder.FailAsync(
                    invocation, AgentInvocationStatus.Failed, exception, cancellationToken);
            }

            inboundEvent.Handling = InboundHandlingKind.NeedsReview;
            inboundEvent.HandledAt = timeProvider.GetUtcNow();
            inboundEvent.Explanation = $"Intake classification failed: {exception.Message}";
            await inboundRepository.UpdateAsync(inboundEvent, cancellationToken);
        }
    }

    private async Task<DomainTask?> FindAppendTargetAsync(
        IntakeResult result,
        IReadOnlyList<DomainTask> candidates,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(result.ExistingTaskId) ||
            !candidates.Any(task => task.Id == result.ExistingTaskId))
        {
            return null;
        }

        var task = await taskRepository.GetByIdAsync(result.ExistingTaskId, cancellationToken);
        return task?.Status is SamwiseTaskStatus.Completed or SamwiseTaskStatus.Discarded ? null : task;
    }

    private static void MarkForReview(InboundEvent inboundEvent, string reason)
    {
        inboundEvent.Handling = InboundHandlingKind.NeedsReview;
        inboundEvent.TaskId = null;
        inboundEvent.Explanation = $"{inboundEvent.Explanation} {reason}".Trim();
    }
}

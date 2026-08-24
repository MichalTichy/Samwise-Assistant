using MediatR;
using Microsoft.Extensions.Logging;
using Samwise.Domain.Agents;
using Samwise.Domain.Prompts;
using Samwise.Domain.Tasks;
using Samwise.Logic.Agents;
using Samwise.Logic.Prompts;
using Samwise.Logic.Tasks.Specifications;
using Shared.Infrastructure.Persistence.Repositories;
using DomainTask = Samwise.Domain.Tasks.Task;
using Task = System.Threading.Tasks.Task;

namespace Samwise.Logic.Tasks.Usecases;

public sealed record PlanTaskUsecase(string TaskId) : IRequest;

internal sealed class PlanTaskUsecaseHandler(
    IRepository<DomainTask> taskRepository,
    IReadOnlyRepository<PromptTemplate> promptRepository,
    IAgentRuntime runtime,
    IPromptRenderer promptRenderer,
    AgentInvocationRecorder invocationRecorder,
    TimeProvider timeProvider,
    ILogger<PlanTaskUsecaseHandler> logger) : IRequestHandler<PlanTaskUsecase>
{
    public async Task Handle(PlanTaskUsecase request, CancellationToken cancellationToken)
    {
        var task = await taskRepository.GetRequiredByIdAsync(request.TaskId, cancellationToken);
        if (task.Status != SamwiseTaskStatus.Planning)
        {
            return;
        }

        var revision = task.Revision;
        var planNumber = task.ReservePlanNumber(timeProvider.GetUtcNow());
        await taskRepository.UpdateAsync(task, cancellationToken);

        AgentInvocation? invocation = null;
        try
        {
            var template = await promptRepository.GetRequiredByIdAsync("planning", cancellationToken);
            var comments = task.Comments;
            var previousPlan = task.FindLatestPlan();
            var planningCatalog = await runtime.GetPlanningToolsAsync(cancellationToken);
            var executionCatalog = await runtime.GetExecutionToolsAsync(cancellationToken);
            var renderResult = promptRenderer.Render(
                PromptStage.Planning,
                template.Content,
                AgentWorkflowSupport.BuildPlanningValues(
                    task,
                    comments,
                    previousPlan,
                    executionCatalog,
                    timeProvider.GetUtcNow()));
            if (!renderResult.Succeeded)
            {
                throw new InvalidOperationException(string.Join(" ", renderResult.Errors));
            }

            invocation = await invocationRecorder.StartAsync(
                "Planning",
                renderResult.Text,
                null,
                task.Id,
                revision,
                cancellationToken);
            var result = await runtime.PlanAsync(
                new PlannerRequest(
                    task.Id,
                    revision,
                    planNumber,
                    renderResult.Text,
                    planningCatalog,
                    executionCatalog),
                cancellationToken);
            AgentWorkflowSupport.ValidatePlan(result.Plan, executionCatalog);
            await invocationRecorder.SucceedAsync(invocation, result.Receipt, cancellationToken);

            var currentTask = await taskRepository.GetRequiredByIdAsync(task.Id, cancellationToken);
            if (currentTask.Revision != revision || currentTask.Status != SamwiseTaskStatus.Planning)
            {
                return;
            }

            var plan = new TaskPlan
            {
                Id = Guid.NewGuid().ToString(),
                TaskId = currentTask.Id,
                TaskRevision = currentTask.Revision,
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
            currentTask.ApplyPlan(plan, timeProvider.GetUtcNow());
            await taskRepository.UpdateAsync(currentTask, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (invocation is not null)
            {
                await invocationRecorder.FailAsync(
                    invocation,
                    AgentInvocationStatus.Failed,
                    new OperationCanceledException("Planning was cancelled."),
                    CancellationToken.None);
            }

            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Planning task {TaskId} failed", task.Id);
            if (invocation is not null)
            {
                await invocationRecorder.FailAsync(
                    invocation,
                    AgentInvocationStatus.Failed,
                    exception,
                    cancellationToken);
            }

            var currentTask = await taskRepository.GetByIdAsync(task.Id, cancellationToken);
            if (currentTask?.Revision == revision)
            {
                currentTask.MarkPlanningFailed(timeProvider.GetUtcNow());
                await taskRepository.UpdateAsync(currentTask, cancellationToken);
            }
        }
    }
}

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

internal sealed record ExecuteTaskUsecase(string TaskId, string PlanId, int TaskRevision) : IRequest;

internal sealed class ExecuteTaskUsecaseHandler(
    IRepository<DomainTask> taskRepository,
    IReadOnlyRepository<PromptTemplate> promptRepository,
    IAgentRuntime runtime,
    IPromptRenderer promptRenderer,
    AgentInvocationRecorder invocationRecorder,
    TimeProvider timeProvider,
    ILogger<ExecuteTaskUsecaseHandler> logger) : IRequestHandler<ExecuteTaskUsecase>
{
    public async Task Handle(ExecuteTaskUsecase request, CancellationToken cancellationToken)
    {
        var task = await taskRepository.GetRequiredByIdAsync(request.TaskId, cancellationToken);
        if (task.Status != SamwiseTaskStatus.Approved ||
            task.Revision != request.TaskRevision ||
            task.CurrentPlanId != request.PlanId)
        {
            return;
        }

        AgentInvocation? invocation = null;
        var executionStarted = false;
        try
        {
            var plan = task.FindCurrentPlan()
                ?? throw new InvalidOperationException("The approved plan is missing from the task document.");
            var template = await promptRepository.GetRequiredByIdAsync("execution", cancellationToken);
            var comments = task.Comments;
            var catalog = await runtime.GetExecutionToolsAsync(cancellationToken);
            if (!string.Equals(plan.ExecutionTools.CatalogVersion, catalog.CatalogVersion, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The execution tool catalog changed after approval. Replanning is required.");
            }

            var rendered = promptRenderer.Render(
                PromptStage.Execution,
                template.Content,
                AgentWorkflowSupport.BuildExecutionValues(task, comments, plan, catalog, timeProvider.GetUtcNow()));
            if (!rendered.Succeeded)
            {
                throw new InvalidOperationException(string.Join(" ", rendered.Errors));
            }

            invocation = await invocationRecorder.StartAsync(
                "Execution", rendered.Text, null, task.Id, task.Revision, cancellationToken);
            task.StartExecution(plan.Id, request.TaskRevision, timeProvider.GetUtcNow());
            await taskRepository.UpdateAsync(task, cancellationToken);
            executionStarted = true;

            var result = await runtime.ExecuteAsync(
                new ExecutionRequest(task.Id, task.Revision, plan.Id, rendered.Text, catalog), cancellationToken);
            await invocationRecorder.SucceedAsync(invocation, result.Receipt, cancellationToken);

            var currentTask = await taskRepository.GetRequiredByIdAsync(task.Id, cancellationToken);
            if (result.Succeeded)
            {
                currentTask.CompleteExecution(timeProvider.GetUtcNow());
            }
            else
            {
                currentTask.MarkExecutionUncertain(timeProvider.GetUtcNow());
            }

            currentTask.AddExecutionUpdate(
                AgentWorkflowSupport.FormatExecutionResult(result),
                timeProvider.GetUtcNow());
            await taskRepository.UpdateAsync(currentTask, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RecordFailureAsync(new OperationCanceledException("Execution was cancelled."), executionStarted);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Executing task {TaskId} failed", task.Id);
            await RecordFailureAsync(exception, executionStarted);
        }

        async Task RecordFailureAsync(Exception exception, bool mayHaveSideEffects)
        {
            if (invocation is not null)
            {
                await invocationRecorder.FailAsync(
                    invocation,
                    mayHaveSideEffects ? AgentInvocationStatus.OutcomeUnknown : AgentInvocationStatus.Failed,
                    exception,
                    CancellationToken.None);
            }

            var currentTask = await taskRepository.GetByIdAsync(task.Id, CancellationToken.None);
            if (currentTask is null)
            {
                return;
            }

            if (mayHaveSideEffects)
            {
                currentTask.MarkExecutionUncertain(timeProvider.GetUtcNow());
            }
            else
            {
                currentTask.MarkApprovedExecutionFailed(timeProvider.GetUtcNow());
            }

            await taskRepository.UpdateAsync(currentTask, CancellationToken.None);
        }
    }
}

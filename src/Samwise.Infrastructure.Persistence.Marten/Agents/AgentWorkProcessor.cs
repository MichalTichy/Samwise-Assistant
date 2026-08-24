using System.Text.Json;
using Marten;
using Microsoft.Extensions.Logging;
using Samwise.Logic.Agents;
using Samwise.Logic.Prompts;
using Samwise.Logic.Time;
using Samwise.Domain.Agents;
using Samwise.Domain.Inbox;
using Samwise.Domain.Prompts;
using Samwise.Domain.Tasks;
using Samwise.Infrastructure.Persistence.Marten.Specifications;

namespace Samwise.Infrastructure.Persistence.Marten.Agents;

internal sealed class AgentWorkProcessor(
    IDocumentStore store,
    IAgentRuntime runtime,
    IPromptRenderer promptRenderer,
    IClock clock,
    ILogger<AgentWorkProcessor> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task RecoverAbandonedWorkAsync(CancellationToken cancellationToken)
    {
        await using var session = store.LightweightSession();
        var abandoned = await session.Query<AgentWorkItem>()
            .Where(item => item.Status == AgentWorkStatus.Running)
            .ToListAsync(cancellationToken);
        if (abandoned.Count == 0)
        {
            return;
        }

        var now = clock.UtcNow;
        foreach (var workItem in abandoned)
        {
            SamwiseTask? task = null;
            if (workItem.TaskId is { } taskId)
            {
                task = await session.LoadAsync<SamwiseTask>(taskId, cancellationToken);
            }

            var executionMayHaveStarted =
                workItem.Kind == AgentWorkKind.ExecuteTask &&
                task?.Status == SamwiseTaskStatus.Executing;
            var safeToRetry =
                workItem.Kind != AgentWorkKind.ExecuteTask ||
                (task?.Status == SamwiseTaskStatus.Approved &&
                 task.Revision == workItem.TaskRevision &&
                 task.CurrentPlanId == workItem.PlanId);
            workItem.Error = executionMayHaveStarted
                ? "Execution was interrupted by an application restart; its external outcome is unknown."
                : "Recovered after an application restart.";

            if (executionMayHaveStarted)
            {
                workItem.Status = AgentWorkStatus.OutcomeUnknown;
                workItem.FinishedAt = now;
                task!.MarkExecutionUncertain(now);
                session.Store(task);
            }
            else if (safeToRetry)
            {
                workItem.Status = AgentWorkStatus.Queued;
                workItem.AvailableAt = now;
                workItem.StartedAt = null;
                workItem.FinishedAt = null;
            }
            else
            {
                workItem.Status = AgentWorkStatus.Succeeded;
                workItem.Error = "Superseded while the application was stopped.";
                workItem.FinishedAt = now;
            }

            var invocations = await session.Query<AgentInvocation>()
                .Where(invocation =>
                    invocation.WorkItemId == workItem.Id &&
                    invocation.Status == AgentInvocationStatus.Running)
                .ToListAsync(cancellationToken);
            foreach (var invocation in invocations)
            {
                invocation.Status = executionMayHaveStarted
                    ? AgentInvocationStatus.OutcomeUnknown
                    : AgentInvocationStatus.Failed;
                invocation.Error = workItem.Error;
                invocation.FinishedAt = now;
                session.Store(invocation);
            }

            session.Store(workItem);
        }

        await session.SaveChangesAsync(cancellationToken);
        logger.LogWarning("Recovered {Count} abandoned agent work item(s)", abandoned.Count);
    }

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var workItem = await TryClaimAsync(cancellationToken);
        if (workItem is null)
        {
            return false;
        }

        try
        {
            switch (workItem.Kind)
            {
                case AgentWorkKind.ClassifyInput:
                    await ClassifyInputAsync(workItem, cancellationToken);
                    break;
                case AgentWorkKind.PlanTask:
                    await PlanAsync(workItem, cancellationToken);
                    break;
                case AgentWorkKind.ExecuteTask:
                    await ExecuteAsync(workItem, cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported work kind '{workItem.Kind}'.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Agent work item {WorkItemId} failed", workItem.Id);
            await MarkFailedAsync(workItem, exception, cancellationToken);
        }

        return true;
    }

    private async Task<AgentWorkItem?> TryClaimAsync(CancellationToken cancellationToken)
    {
        await using var session = store.LightweightSession();
        var now = clock.UtcNow;
        var workItem = await session.Query<AgentWorkItem>()
            .Where(item => item.Status == AgentWorkStatus.Queued && item.AvailableAt <= now)
            .OrderBy(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (workItem is null)
        {
            return null;
        }

        workItem.Status = AgentWorkStatus.Running;
        workItem.Attempts++;
        workItem.StartedAt = now;
        workItem.FinishedAt = null;
        workItem.Error = null;
        session.Store(workItem);
        await session.SaveChangesAsync(cancellationToken);
        return workItem;
    }

    private async Task ClassifyInputAsync(
        AgentWorkItem workItem,
        CancellationToken cancellationToken)
    {
        var inboundEventId = workItem.InboundEventId
            ?? throw new InvalidOperationException("Intake work has no inbound event ID.");

        IntakeData data;
        await using (var session = store.LightweightSession())
        {
            var inboundEvent = await session.LoadAsync<InboundEvent>(inboundEventId, cancellationToken)
                ?? throw new InvalidOperationException("The inbound event no longer exists.");
            if (inboundEvent.Handling != InboundHandlingKind.Pending)
            {
                await MarkSupersededAsync(workItem.Id, cancellationToken);
                return;
            }

            var template = await session.LoadAsync<PromptTemplate>("intake", cancellationToken)
                ?? throw new InvalidOperationException("The intake prompt is missing.");
            var candidates = await new ActiveTaskCandidatesSpecification()
                .ApplyAsync(session.Query<SamwiseTask>(), cancellationToken);
            data = new IntakeData(inboundEvent, template, candidates);
        }

        var renderResult = promptRenderer.Render(
            PromptStage.Intake,
            data.Template.Content,
            BuildIntakeValues(data));
        if (!renderResult.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", renderResult.Errors));
        }

        var invocation = await StartInvocationAsync(
            workItem,
            "Intake",
            renderResult.Text,
            cancellationToken);

        IntakeResult result;
        try
        {
            result = await runtime.ClassifyAsync(
                new IntakeRequest(
                    data.InboundEvent.Id,
                    data.InboundEvent.Source,
                    renderResult.Text),
                cancellationToken);
        }
        catch (Exception exception)
        {
            await FinishInvocationAsync(
                invocation.Id,
                AgentInvocationStatus.Failed,
                null,
                exception,
                cancellationToken);
            throw;
        }

        await using var finalize = store.LightweightSession();
        var currentEvent = await finalize.LoadAsync<InboundEvent>(inboundEventId, cancellationToken)
            ?? throw new InvalidOperationException("The inbound event no longer exists.");
        var currentWork = await finalize.LoadAsync<AgentWorkItem>(workItem.Id, cancellationToken)
            ?? throw new InvalidOperationException("The work item no longer exists.");
        var currentInvocation = await finalize.LoadAsync<AgentInvocation>(invocation.Id, cancellationToken)
            ?? throw new InvalidOperationException("The invocation no longer exists.");
        var now = clock.UtcNow;

        currentInvocation.Provider = result.Receipt.Provider;
        currentInvocation.Model = result.Receipt.Model;
        currentInvocation.CompletionId = result.Receipt.CompletionId;
        currentInvocation.RawResponse = result.Receipt.RawResponse;
        currentInvocation.Status = AgentInvocationStatus.Succeeded;
        currentInvocation.FinishedAt = now;

        if (currentEvent.Handling == InboundHandlingKind.Pending)
        {
            await ApplyIntakeDecisionAsync(
                finalize,
                currentEvent,
                result,
                data.Candidates.Select(task => task.Id).ToHashSet(),
                now,
                cancellationToken);
        }
        else
        {
            currentWork.Error = "Superseded because the inbound event was already handled.";
        }

        currentWork.Status = AgentWorkStatus.Succeeded;
        currentWork.FinishedAt = now;
        finalize.Store(currentEvent);
        finalize.Store(currentWork);
        finalize.Store(currentInvocation);
        await finalize.SaveChangesAsync(cancellationToken);
    }

    private static async Task ApplyIntakeDecisionAsync(
        IDocumentSession session,
        InboundEvent inboundEvent,
        IntakeResult result,
        IReadOnlySet<Guid> candidateTaskIds,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        inboundEvent.HandledAt = now;
        inboundEvent.Explanation = string.IsNullOrWhiteSpace(result.Explanation)
            ? "Intake completed without an explanation."
            : result.Explanation.Trim();

        switch (result.Decision)
        {
            case IntakeDecision.CreateTask:
                {
                    var task = CreateTaskFromIntake(inboundEvent, result, now);
                    inboundEvent.Handling = InboundHandlingKind.CreateTask;
                    inboundEvent.TaskId = task.Id;
                    session.Store(task);
                    session.Store(CreateTaskWorkItem(AgentWorkKind.PlanTask, task, now));
                    break;
                }
            case IntakeDecision.AppendToTask:
                {
                    if (result.ExistingTaskId is not { } existingTaskId)
                    {
                        MarkForReview(inboundEvent, "The classifier chose an existing task but did not provide its ID.");
                        break;
                    }

                    if (!candidateTaskIds.Contains(existingTaskId))
                    {
                        MarkForReview(inboundEvent, "The classifier selected a task that was not in its candidate list.");
                        break;
                    }

                    var task = await session.LoadAsync<SamwiseTask>(existingTaskId, cancellationToken);
                    if (task is null || task.Status is SamwiseTaskStatus.Completed or SamwiseTaskStatus.Discarded)
                    {
                        MarkForReview(inboundEvent, "The classifier selected a task that is missing or no longer active.");
                        break;
                    }

                    task.AddContext(inboundEvent.Text, now);
                    inboundEvent.Handling = InboundHandlingKind.AppendToTask;
                    inboundEvent.TaskId = task.Id;
                    session.Store(task);
                    session.Store(new TaskComment
                    {
                        Id = Guid.NewGuid(),
                        TaskId = task.Id,
                        Body = inboundEvent.Text,
                        AuthorKind = CommentAuthorKind.User,
                        Kind = TaskCommentKind.Context,
                        CreatedAt = now
                    });
                    session.Store(CreateTaskWorkItem(AgentWorkKind.PlanTask, task, now));
                    break;
                }
            case IntakeDecision.Ignore:
                inboundEvent.Handling = InboundHandlingKind.Ignore;
                break;
            case IntakeDecision.NeedsReview:
                inboundEvent.Handling = InboundHandlingKind.NeedsReview;
                break;
            default:
                throw new InvalidOperationException($"Unsupported intake decision '{result.Decision}'.");
        }
    }

    private static void MarkForReview(InboundEvent inboundEvent, string reason)
    {
        inboundEvent.Handling = InboundHandlingKind.NeedsReview;
        inboundEvent.TaskId = null;
        inboundEvent.Explanation = $"{inboundEvent.Explanation} {reason}".Trim();
    }

    private async Task PlanAsync(AgentWorkItem workItem, CancellationToken cancellationToken)
    {
        var taskId = RequireTaskId(workItem);
        PlanningData data;
        await using (var session = store.LightweightSession())
        {
            var task = await session.LoadAsync<SamwiseTask>(taskId, cancellationToken)
                ?? throw new InvalidOperationException("The task no longer exists.");
            if (task.Revision != workItem.TaskRevision || task.Status == SamwiseTaskStatus.Discarded)
            {
                await MarkSupersededAsync(workItem.Id, cancellationToken);
                return;
            }

            var template = await session.LoadAsync<PromptTemplate>("planning", cancellationToken)
                ?? throw new InvalidOperationException("The planning prompt is missing.");
            var comments = await new TaskCommentsSpecification(task.Id)
                .ApplyAsync(session.Query<TaskComment>(), cancellationToken);
            var previousPlan = await session.Query<TaskPlan>()
                .Where(plan => plan.TaskId == task.Id)
                .OrderByDescending(plan => plan.PlanNumber)
                .FirstOrDefaultAsync(cancellationToken);
            var planNumber = task.ReservePlanNumber(clock.UtcNow);
            session.Store(task);
            await session.SaveChangesAsync(cancellationToken);
            data = new PlanningData(task, template, comments, previousPlan, planNumber);
        }

        var planningCatalog = await runtime.GetPlanningToolsAsync(cancellationToken);
        var executionCatalog = await runtime.GetExecutionToolsAsync(cancellationToken);
        var renderResult = promptRenderer.Render(
            PromptStage.Planning,
            data.Template.Content,
            BuildPlanningValues(data, executionCatalog));
        if (!renderResult.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", renderResult.Errors));
        }

        var invocation = await StartInvocationAsync(
            workItem,
            "Planning",
            renderResult.Text,
            cancellationToken);
        PlannerResult result;
        try
        {
            result = await runtime.PlanAsync(
                new PlannerRequest(
                    data.Task.Id,
                    data.Task.Revision,
                    data.PlanNumber,
                    renderResult.Text,
                    planningCatalog,
                    executionCatalog),
                cancellationToken);
            ValidatePlan(result.Plan, executionCatalog);
        }
        catch (Exception exception)
        {
            await FinishInvocationAsync(invocation.Id, AgentInvocationStatus.Failed, null, exception, cancellationToken);
            throw;
        }

        await using var finalize = store.LightweightSession();
        var currentTask = await finalize.LoadAsync<SamwiseTask>(taskId, cancellationToken)
            ?? throw new InvalidOperationException("The task no longer exists.");
        var currentWork = await finalize.LoadAsync<AgentWorkItem>(workItem.Id, cancellationToken)
            ?? throw new InvalidOperationException("The work item no longer exists.");
        var currentInvocation = await finalize.LoadAsync<AgentInvocation>(invocation.Id, cancellationToken)
            ?? throw new InvalidOperationException("The invocation no longer exists.");

        currentInvocation.Provider = result.Receipt.Provider;
        currentInvocation.Model = result.Receipt.Model;
        currentInvocation.CompletionId = result.Receipt.CompletionId;
        currentInvocation.RawResponse = result.Receipt.RawResponse;
        currentInvocation.Status = AgentInvocationStatus.Succeeded;
        currentInvocation.FinishedAt = clock.UtcNow;

        if (currentTask.Revision == workItem.TaskRevision && currentTask.Status == SamwiseTaskStatus.Planning)
        {
            var plan = new TaskPlan
            {
                Id = Guid.NewGuid(),
                TaskId = currentTask.Id,
                TaskRevision = currentTask.Revision,
                PlanNumber = data.PlanNumber,
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
                PlanningToolCatalogVersion = planningCatalog.CatalogVersion,
                ExecutionToolCatalogVersion = executionCatalog.CatalogVersion,
                PromptTemplateId = data.Template.Id,
                RenderedPrompt = renderResult.Text,
                CreatedAt = clock.UtcNow
            };
            currentTask.ApplyPlan(plan, clock.UtcNow);
            finalize.Store(plan);
            finalize.Store(currentTask);
        }
        else
        {
            currentWork.Error = "Superseded by a newer task revision.";
        }

        currentWork.Status = AgentWorkStatus.Succeeded;
        currentWork.FinishedAt = clock.UtcNow;
        finalize.Store(currentWork);
        finalize.Store(currentInvocation);
        await finalize.SaveChangesAsync(cancellationToken);
    }

    private async Task ExecuteAsync(AgentWorkItem workItem, CancellationToken cancellationToken)
    {
        var taskId = RequireTaskId(workItem);
        ExecutionData data;
        await using (var session = store.LightweightSession())
        {
            var task = await session.LoadAsync<SamwiseTask>(taskId, cancellationToken)
                ?? throw new InvalidOperationException("The task no longer exists.");
            if (task.Revision != workItem.TaskRevision ||
                task.Status != SamwiseTaskStatus.Approved ||
                task.CurrentPlanId != workItem.PlanId)
            {
                await MarkSupersededAsync(workItem.Id, cancellationToken);
                return;
            }

            var plan = await session.LoadAsync<TaskPlan>(workItem.PlanId!.Value, cancellationToken)
                ?? throw new InvalidOperationException("The approved plan no longer exists.");
            var template = await session.LoadAsync<PromptTemplate>("execution", cancellationToken)
                ?? throw new InvalidOperationException("The execution prompt is missing.");
            var comments = await new TaskCommentsSpecification(task.Id)
                .ApplyAsync(session.Query<TaskComment>(), cancellationToken);
            data = new ExecutionData(task, plan, template, comments);
        }

        var executionCatalog = await runtime.GetExecutionToolsAsync(cancellationToken);
        if (!string.Equals(
                data.Plan.ExecutionToolCatalogVersion,
                executionCatalog.CatalogVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The execution tool catalog changed after approval. Replanning is required.");
        }

        var renderResult = promptRenderer.Render(
            PromptStage.Execution,
            data.Template.Content,
            BuildExecutionValues(data, executionCatalog));
        if (!renderResult.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", renderResult.Errors));
        }

        var invocation = await StartInvocationAsync(
            workItem,
            "Execution",
            renderResult.Text,
            cancellationToken);

        await using (var start = store.LightweightSession())
        {
            var currentTask = await start.LoadAsync<SamwiseTask>(taskId, cancellationToken)
                ?? throw new InvalidOperationException("The task no longer exists.");
            currentTask.StartExecution(data.Plan.Id, workItem.TaskRevision, clock.UtcNow);
            start.Store(currentTask);
            await start.SaveChangesAsync(cancellationToken);
        }

        ExecutionResult result;
        try
        {
            result = await runtime.ExecuteAsync(
                new ExecutionRequest(
                    data.Task.Id,
                    data.Task.Revision,
                    data.Plan.Id,
                    renderResult.Text,
                    executionCatalog),
                cancellationToken);
        }
        catch (Exception exception)
        {
            await FinishInvocationAsync(invocation.Id, AgentInvocationStatus.OutcomeUnknown, null, exception, cancellationToken);
            throw;
        }

        await using var finalize = store.LightweightSession();
        var taskToFinish = await finalize.LoadAsync<SamwiseTask>(taskId, cancellationToken)
            ?? throw new InvalidOperationException("The task no longer exists.");
        var workToFinish = await finalize.LoadAsync<AgentWorkItem>(workItem.Id, cancellationToken)
            ?? throw new InvalidOperationException("The work item no longer exists.");
        var invocationToFinish = await finalize.LoadAsync<AgentInvocation>(invocation.Id, cancellationToken)
            ?? throw new InvalidOperationException("The invocation no longer exists.");

        invocationToFinish.Provider = result.Receipt.Provider;
        invocationToFinish.Model = result.Receipt.Model;
        invocationToFinish.CompletionId = result.Receipt.CompletionId;
        invocationToFinish.RawResponse = result.Receipt.RawResponse;
        invocationToFinish.Status = AgentInvocationStatus.Succeeded;
        invocationToFinish.FinishedAt = clock.UtcNow;

        if (result.Succeeded)
        {
            taskToFinish.CompleteExecution(clock.UtcNow);
            workToFinish.Status = AgentWorkStatus.Succeeded;
        }
        else
        {
            taskToFinish.MarkExecutionUncertain(clock.UtcNow);
            workToFinish.Status = AgentWorkStatus.Failed;
            workToFinish.Error = result.Summary;
        }

        workToFinish.FinishedAt = clock.UtcNow;
        finalize.Store(taskToFinish);
        finalize.Store(workToFinish);
        finalize.Store(invocationToFinish);
        finalize.Store(new TaskComment
        {
            Id = Guid.NewGuid(),
            TaskId = taskToFinish.Id,
            AuthorKind = CommentAuthorKind.Samwise,
            Kind = TaskCommentKind.ExecutionUpdate,
            Body = FormatExecutionResult(result),
            CreatedAt = clock.UtcNow
        });
        await finalize.SaveChangesAsync(cancellationToken);
    }

    private async Task<AgentInvocation> StartInvocationAsync(
        AgentWorkItem workItem,
        string profile,
        string renderedPrompt,
        CancellationToken cancellationToken)
    {
        var invocation = new AgentInvocation
        {
            Id = Guid.NewGuid(),
            InboundEventId = workItem.InboundEventId,
            TaskId = workItem.TaskId,
            TaskRevision = workItem.TaskRevision,
            WorkItemId = workItem.Id,
            Profile = profile,
            Provider = "Pending",
            RenderedPrompt = renderedPrompt,
            Status = AgentInvocationStatus.Running,
            StartedAt = clock.UtcNow
        };

        await using var session = store.LightweightSession();
        session.Store(invocation);
        await session.SaveChangesAsync(cancellationToken);
        return invocation;
    }

    private async Task FinishInvocationAsync(
        Guid invocationId,
        AgentInvocationStatus status,
        AgentReceipt? receipt,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        await using var session = store.LightweightSession();
        var invocation = await session.LoadAsync<AgentInvocation>(invocationId, cancellationToken);
        if (invocation is null)
        {
            return;
        }

        invocation.Status = status;
        invocation.FinishedAt = clock.UtcNow;
        invocation.Error = exception?.Message;
        if (receipt is not null)
        {
            invocation.Provider = receipt.Provider;
            invocation.Model = receipt.Model;
            invocation.CompletionId = receipt.CompletionId;
            invocation.RawResponse = receipt.RawResponse;
        }

        session.Store(invocation);
        await session.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkFailedAsync(
        AgentWorkItem workItem,
        Exception exception,
        CancellationToken cancellationToken)
    {
        await using var session = store.LightweightSession();
        var currentWork = await session.LoadAsync<AgentWorkItem>(workItem.Id, cancellationToken);
        SamwiseTask? task = null;
        if (workItem.TaskId is { } taskId)
        {
            task = await session.LoadAsync<SamwiseTask>(taskId, cancellationToken);
        }
        if (currentWork is null)
        {
            return;
        }

        var executionMayHaveStarted = workItem.Kind == AgentWorkKind.ExecuteTask &&
            task?.Status == SamwiseTaskStatus.Executing;
        currentWork.Status = executionMayHaveStarted
            ? AgentWorkStatus.OutcomeUnknown
            : AgentWorkStatus.Failed;
        currentWork.Error = exception.Message;
        currentWork.FinishedAt = clock.UtcNow;
        session.Store(currentWork);

        var runningInvocations = await session.Query<AgentInvocation>()
            .Where(invocation =>
                invocation.WorkItemId == workItem.Id &&
                invocation.Status == AgentInvocationStatus.Running)
            .ToListAsync(cancellationToken);
        foreach (var invocation in runningInvocations)
        {
            invocation.Status = executionMayHaveStarted
                ? AgentInvocationStatus.OutcomeUnknown
                : AgentInvocationStatus.Failed;
            invocation.Error = exception.Message;
            invocation.FinishedAt = clock.UtcNow;
            session.Store(invocation);
        }

        if (workItem.Kind == AgentWorkKind.ClassifyInput && workItem.InboundEventId is { } inboundEventId)
        {
            var inboundEvent = await session.LoadAsync<InboundEvent>(inboundEventId, cancellationToken);
            if (inboundEvent?.Handling == InboundHandlingKind.Pending)
            {
                inboundEvent.Handling = InboundHandlingKind.NeedsReview;
                inboundEvent.HandledAt = clock.UtcNow;
                inboundEvent.Explanation = $"Intake classification failed: {exception.Message}";
                session.Store(inboundEvent);
            }
        }

        if (task is not null && task.Revision == workItem.TaskRevision)
        {
            if (executionMayHaveStarted)
            {
                task.MarkExecutionUncertain(clock.UtcNow);
            }
            else
            {
                task.MarkPlanningFailed(clock.UtcNow);
                if (task.Status == SamwiseTaskStatus.Approved)
                {
                    task.Status = SamwiseTaskStatus.NeedsAttention;
                    task.UpdatedAt = clock.UtcNow;
                }
            }

            session.Store(task);
        }

        await session.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkSupersededAsync(Guid workItemId, CancellationToken cancellationToken)
    {
        await using var session = store.LightweightSession();
        var workItem = await session.LoadAsync<AgentWorkItem>(workItemId, cancellationToken);
        if (workItem is null)
        {
            return;
        }

        workItem.Status = AgentWorkStatus.Succeeded;
        workItem.Error = "Superseded by newer task state.";
        workItem.FinishedAt = clock.UtcNow;
        session.Store(workItem);
        await session.SaveChangesAsync(cancellationToken);
    }

    private IReadOnlyDictionary<string, string?> BuildPlanningValues(
        PlanningData data,
        ToolCatalogSnapshot executionCatalog) =>
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["task.id"] = data.Task.Id.ToString(),
            ["task.title"] = data.Task.Title,
            ["task.description"] = data.Task.Description,
            ["task.revision"] = data.Task.Revision.ToString(),
            ["task.comments"] = FormatComments(data.Comments),
            ["previous_plan"] = data.PreviousPlan is null ? string.Empty : JsonSerializer.Serialize(data.PreviousPlan, JsonOptions),
            ["rejection_feedback"] = data.Task.LatestPlanningFeedback,
            ["execution_tools"] = executionCatalog.RawJson,
            ["current_time"] = clock.UtcNow.ToString("O")
        };

    private IReadOnlyDictionary<string, string?> BuildIntakeValues(IntakeData data) =>
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["input.text"] = data.InboundEvent.Text,
            ["active_tasks"] = JsonSerializer.Serialize(
                data.Candidates.Select(task => new
                {
                    id = task.Id,
                    title = task.Title,
                    description = Truncate(task.Description, 1_000),
                    status = task.Status.ToString(),
                    revision = task.Revision,
                    updated_at = task.UpdatedAt
                }),
                JsonOptions),
            ["current_time"] = clock.UtcNow.ToString("O")
        };

    private IReadOnlyDictionary<string, string?> BuildExecutionValues(
        ExecutionData data,
        ToolCatalogSnapshot executionCatalog) =>
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["task.id"] = data.Task.Id.ToString(),
            ["task.title"] = data.Task.Title,
            ["task.description"] = data.Task.Description,
            ["task.revision"] = data.Task.Revision.ToString(),
            ["task.comments"] = FormatComments(data.Comments),
            ["approved_plan"] = JsonSerializer.Serialize(data.Plan, JsonOptions),
            ["execution_tools"] = executionCatalog.RawJson,
            ["current_time"] = clock.UtcNow.ToString("O")
        };

    private static string FormatComments(IReadOnlyList<TaskComment> comments) => comments.Count == 0
        ? string.Empty
        : string.Join(Environment.NewLine, comments.Select(comment => $"- [{comment.AuthorKind}] {comment.Body}"));

    internal static SamwiseTask CreateTaskFromIntake(
        InboundEvent inboundEvent,
        IntakeResult result,
        DateTimeOffset now) =>
        SamwiseTask.CreateFromInbound(
            inboundEvent.Text,
            result.ProposedTitle,
            now);

    internal static string FormatExecutionResult(ExecutionResult result) => result.Outputs.Count == 0
        ? result.Summary
        : string.Concat(
            result.Summary,
            Environment.NewLine,
            Environment.NewLine,
            "Outputs:",
            Environment.NewLine,
            string.Join(Environment.NewLine, result.Outputs));

    private static string Truncate(string value, int maximumLength) => value.Length <= maximumLength
        ? value
        : value[..maximumLength] + "…";

    private static void ValidatePlan(PlanDraft plan, ToolCatalogSnapshot executionCatalog)
    {
        if (string.IsNullOrWhiteSpace(plan.Summary) || plan.Steps.Count == 0)
        {
            throw new InvalidOperationException("The planner returned an empty plan.");
        }

        var expectedOrder = 1;
        var invocableTools = executionCatalog.Tools
            .Where(tool => tool.CanInvoke)
            .Select(tool => tool.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var step in plan.Steps.OrderBy(step => step.Order))
        {
            if (step.Order != expectedOrder++)
            {
                throw new InvalidOperationException("Plan step numbers must be consecutive and start at one.");
            }

            if (string.IsNullOrWhiteSpace(step.Title) ||
                string.IsNullOrWhiteSpace(step.TechnicalDescription) ||
                string.IsNullOrWhiteSpace(step.SuccessCriteria))
            {
                throw new InvalidOperationException("Every plan step needs a title, technical description, and success criterion.");
            }

            var unavailableTool = step.ToolIds.FirstOrDefault(toolId => !invocableTools.Contains(toolId));
            if (unavailableTool is not null)
            {
                throw new InvalidOperationException(
                    $"The plan references execution tool '{unavailableTool}', which is not currently invocable.");
            }
        }
    }

    private static Guid RequireTaskId(AgentWorkItem workItem) => workItem.TaskId
        ?? throw new InvalidOperationException($"{workItem.Kind} work has no task ID.");

    private static AgentWorkItem CreateTaskWorkItem(
        AgentWorkKind kind,
        SamwiseTask task,
        DateTimeOffset now,
        Guid? planId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            TaskId = task.Id,
            TaskRevision = task.Revision,
            PlanId = planId,
            Status = AgentWorkStatus.Queued,
            CreatedAt = now,
            AvailableAt = now
        };

    private sealed record IntakeData(
        InboundEvent InboundEvent,
        PromptTemplate Template,
        IReadOnlyList<SamwiseTask> Candidates);

    private sealed record PlanningData(
        SamwiseTask Task,
        PromptTemplate Template,
        IReadOnlyList<TaskComment> Comments,
        TaskPlan? PreviousPlan,
        int PlanNumber);

    private sealed record ExecutionData(
        SamwiseTask Task,
        TaskPlan Plan,
        PromptTemplate Template,
        IReadOnlyList<TaskComment> Comments);
}

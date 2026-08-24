using Marten;
using Samwise.Logic.Tasks;
using Samwise.Logic.Time;
using Samwise.Domain.Agents;
using Samwise.Domain.Inbox;
using Samwise.Domain.Tasks;
using Samwise.Infrastructure.Persistence.Marten.Specifications;

namespace Samwise.Infrastructure.Persistence.Marten.Tasks;

internal sealed class MartenTaskStore(IDocumentStore store, IClock clock) : ITaskStore
{
    public async Task<IReadOnlyList<TaskCard>> GetBoardAsync(
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        await using var session = store.LightweightSession();
        var tasks = await new BoardTasksSpecification(includeArchived)
            .ApplyAsync(session.Query<SamwiseTask>(), cancellationToken);

        var result = new List<TaskCard>(tasks.Count);
        foreach (var task in tasks)
        {
            TaskPlan? plan = null;
            if (task.CurrentPlanId is { } planId)
            {
                plan = await session.LoadAsync<TaskPlan>(planId, cancellationToken);
            }

            result.Add(new TaskCard(
                task.Id,
                task.Title,
                task.Description,
                task.Status,
                task.Revision,
                task.UpdatedAt,
                plan?.PlanNumber));
        }

        return result;
    }

    public async Task<TaskDetails?> GetTaskAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        await using var session = store.LightweightSession();
        var task = await session.LoadAsync<SamwiseTask>(taskId, cancellationToken);
        if (task is null)
        {
            return null;
        }

        var plan = task.CurrentPlanId is { } planId
            ? await session.LoadAsync<TaskPlan>(planId, cancellationToken)
            : null;

        var comments = await new TaskCommentsSpecification(taskId)
            .ApplyAsync(session.Query<TaskComment>(), cancellationToken);
        var decisions = await new TaskDecisionsSpecification(taskId)
            .ApplyAsync(session.Query<PlanDecision>(), cancellationToken);
        var invocations = await new TaskInvocationsSpecification(taskId)
            .ApplyAsync(session.Query<AgentInvocation>(), cancellationToken);

        return new TaskDetails(task, plan, comments, decisions, invocations);
    }

    public async Task<IReadOnlyList<InboxEventItem>> GetInboxAsync(CancellationToken cancellationToken = default)
    {
        await using var session = store.LightweightSession();
        var events = await new InboxEventsSpecification()
            .ApplyAsync(session.Query<InboundEvent>(), cancellationToken);

        return events.Select(item => new InboxEventItem(
            item.Id,
            item.Source,
            item.Text,
            item.ReceivedAt,
            item.Handling,
            item.TaskId,
            item.Explanation)).ToList();
    }

    public async Task AddContextAsync(
        Guid taskId,
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        await using var session = store.LightweightSession();
        var task = await LoadRequiredTaskAsync(session, taskId, cancellationToken);
        var now = clock.UtcNow;
        task.AddContext(text, now);

        session.Store(task);
        session.Store(new TaskComment
        {
            Id = Guid.NewGuid(),
            TaskId = task.Id,
            Body = text.Trim(),
            AuthorKind = CommentAuthorKind.User,
            Kind = TaskCommentKind.Context,
            CreatedAt = now
        });
        session.Store(CreateWorkItem(AgentWorkKind.PlanTask, task, now));
        await session.SaveChangesAsync(cancellationToken);
    }

    public async Task ApprovePlanAsync(
        Guid taskId,
        Guid planId,
        int taskRevision,
        CancellationToken cancellationToken = default)
    {
        await using var session = store.LightweightSession();
        var task = await LoadRequiredTaskAsync(session, taskId, cancellationToken);
        _ = await LoadRequiredPlanAsync(session, planId, taskId, cancellationToken);
        var now = clock.UtcNow;
        task.Approve(planId, taskRevision, now);

        session.Store(task);
        session.Store(new PlanDecision
        {
            Id = Guid.NewGuid(),
            TaskId = task.Id,
            PlanId = planId,
            TaskRevision = taskRevision,
            Decision = PlanDecisionKind.Approved,
            DecidedAt = now
        });
        session.Store(CreateWorkItem(AgentWorkKind.ExecuteTask, task, now, planId));
        await session.SaveChangesAsync(cancellationToken);
    }

    public async Task RejectPlanAsync(
        Guid taskId,
        Guid planId,
        int taskRevision,
        string feedback,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feedback);
        await using var session = store.LightweightSession();
        var task = await LoadRequiredTaskAsync(session, taskId, cancellationToken);
        _ = await LoadRequiredPlanAsync(session, planId, taskId, cancellationToken);
        var now = clock.UtcNow;
        task.Reject(planId, taskRevision, feedback, now);

        session.Store(task);
        session.Store(new PlanDecision
        {
            Id = Guid.NewGuid(),
            TaskId = task.Id,
            PlanId = planId,
            TaskRevision = taskRevision,
            Decision = PlanDecisionKind.Rejected,
            Feedback = feedback.Trim(),
            DecidedAt = now
        });
        session.Store(new TaskComment
        {
            Id = Guid.NewGuid(),
            TaskId = task.Id,
            Body = feedback.Trim(),
            AuthorKind = CommentAuthorKind.User,
            Kind = TaskCommentKind.PlanningFeedback,
            CreatedAt = now
        });
        session.Store(CreateWorkItem(AgentWorkKind.PlanTask, task, now));
        await session.SaveChangesAsync(cancellationToken);
    }

    public async Task DiscardTaskAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        await using var session = store.LightweightSession();
        var task = await LoadRequiredTaskAsync(session, taskId, cancellationToken);
        var now = clock.UtcNow;
        var planId = task.CurrentPlanId;
        var revision = task.Revision;
        task.Discard(now);

        session.Store(task);
        session.Store(new PlanDecision
        {
            Id = Guid.NewGuid(),
            TaskId = task.Id,
            PlanId = planId,
            TaskRevision = revision,
            Decision = PlanDecisionKind.Discarded,
            DecidedAt = now
        });
        await session.SaveChangesAsync(cancellationToken);
    }

    public async Task RetryPlanningAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        await using var session = store.LightweightSession();
        var task = await LoadRequiredTaskAsync(session, taskId, cancellationToken);
        if (task.Status != SamwiseTaskStatus.NeedsAttention)
        {
            throw new InvalidOperationException("Only a task needing attention can retry planning.");
        }

        var exists = await session.Query<AgentWorkItem>().AnyAsync(item =>
            item.TaskId == taskId &&
            item.TaskRevision == task.Revision &&
            item.Kind == AgentWorkKind.PlanTask &&
            (item.Status == AgentWorkStatus.Queued || item.Status == AgentWorkStatus.Running),
            cancellationToken);

        if (!exists)
        {
            var now = clock.UtcNow;
            task.RetryPlanning(now);
            session.Store(task);
            session.Store(CreateWorkItem(AgentWorkKind.PlanTask, task, now));
            await session.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task<SamwiseTask> LoadRequiredTaskAsync(
        IQuerySession session,
        Guid taskId,
        CancellationToken cancellationToken) =>
        await session.LoadAsync<SamwiseTask>(taskId, cancellationToken)
        ?? throw new KeyNotFoundException($"Task '{taskId}' was not found.");

    private static async Task<TaskPlan> LoadRequiredPlanAsync(
        IQuerySession session,
        Guid planId,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        var plan = await session.LoadAsync<TaskPlan>(planId, cancellationToken)
            ?? throw new KeyNotFoundException($"Plan '{planId}' was not found.");
        return plan.TaskId == taskId
            ? plan
            : throw new InvalidOperationException("The plan belongs to a different task.");
    }

    private static AgentWorkItem CreateWorkItem(
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
}

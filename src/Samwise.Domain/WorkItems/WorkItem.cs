using JasperFx;
using Shared.Infrastructure.Persistence;

namespace Samwise.Domain.WorkItems;

public sealed class WorkItem : IItemWithId, IRevisioned
{
    public string Id { get; set; } = string.Empty;
    public int Version { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public WorkItemStatus Status { get; set; }
    public int Revision { get; set; }
    public int NextPlanNumber { get; set; }
    public string? CurrentPlanId { get; set; }
    public string? LatestPlanningFeedback { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public WorkItemCompletion? Completion { get; set; }
    public DateTimeOffset? DiscardedAt { get; set; }
    public string? DiscardedFrom { get; set; }
    public List<WorkItemPlan> Plans { get; set; } = [];
    public List<WorkItemComment> Comments { get; set; } = [];

    public static WorkItem CreateFromInbound(
        string description,
        string? proposedTitle,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        var normalized = description.Trim();
        var normalizedTitle = string.IsNullOrWhiteSpace(proposedTitle)
            ? normalized
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? normalized
            : proposedTitle.Trim();

        return new WorkItem
        {
            Id = Guid.NewGuid().ToString(),
            Title = normalizedTitle.Length <= 96 ? normalizedTitle : normalizedTitle[..93] + "...",
            Description = normalized,
            Status = WorkItemStatus.Planning,
            Revision = 1,
            NextPlanNumber = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public int ReservePlanNumber(DateTimeOffset now)
    {
        if (Status != WorkItemStatus.Planning)
        {
            throw new InvalidOperationException("Only a work item that is planning can reserve a plan number.");
        }

        UpdatedAt = now;
        return NextPlanNumber++;
    }

    public void ApplyPlan(WorkItemPlan plan, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.WorkItemId != Id || plan.WorkItemRevision != Revision)
        {
            throw new InvalidOperationException("The plan does not belong to the current work item revision.");
        }

        if (plan.Review is not null)
        {
            throw new InvalidOperationException("A newly generated plan cannot already have a review.");
        }

        if (Plans.Any(existing => existing.Id == plan.Id))
        {
            throw new InvalidOperationException("The plan is already attached to this work item.");
        }

        Plans.Add(plan);
        CurrentPlanId = plan.Id;
        Status = WorkItemStatus.AwaitingApproval;
        LatestPlanningFeedback = null;
        UpdatedAt = now;
    }

    public void Approve(string planId, int workItemRevision, DateTimeOffset now, string source = "Web")
    {
        if (Status != WorkItemStatus.AwaitingApproval || CurrentPlanId != planId || Revision != workItemRevision)
        {
            throw new InvalidOperationException("Only the current plan can be approved.");
        }

        SetPlanReview(planId, PlanReviewKind.Approved, null, source, now);
        Status = WorkItemStatus.Approved;
        UpdatedAt = now;
    }

    public void Reject(
        string planId,
        int workItemRevision,
        string feedback,
        DateTimeOffset now,
        string source = "Web")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feedback);
        if (Status != WorkItemStatus.AwaitingApproval || CurrentPlanId != planId || Revision != workItemRevision)
        {
            throw new InvalidOperationException("Only the current plan can be rejected.");
        }

        var normalizedFeedback = feedback.Trim();
        SetPlanReview(planId, PlanReviewKind.Rejected, normalizedFeedback, source, now);
        Comments.Add(CreateComment(
            normalizedFeedback,
            CommentAuthorKind.User,
            WorkItemCommentKind.PlanningFeedback,
            now));
        Revision++;
        CurrentPlanId = null;
        LatestPlanningFeedback = normalizedFeedback;
        Status = WorkItemStatus.Planning;
        UpdatedAt = now;
    }

    public void AddContext(
        string text,
        DateTimeOffset now,
        CommentAuthorKind authorKind = CommentAuthorKind.User)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        EnsureEditable();
        var normalizedText = text.Trim();
        Comments.Add(CreateComment(normalizedText, authorKind, WorkItemCommentKind.Context, now));
        Revision++;
        CurrentPlanId = null;
        LatestPlanningFeedback = normalizedText;
        Status = WorkItemStatus.Planning;
        UpdatedAt = now;
    }

    public void MarkPlanningFailed(DateTimeOffset now)
    {
        if (Status == WorkItemStatus.Planning)
        {
            Status = WorkItemStatus.NeedsAttention;
            UpdatedAt = now;
        }
    }

    public void Replan(DateTimeOffset now)
    {
        if (Status is not (WorkItemStatus.AwaitingApproval or WorkItemStatus.NeedsAttention))
        {
            throw new InvalidOperationException("Only a work item awaiting approval or needing attention can be replanned.");
        }

        CurrentPlanId = null;
        Status = WorkItemStatus.Planning;
        UpdatedAt = now;
    }

    public void StartExecution(string planId, int workItemRevision, DateTimeOffset now)
    {
        if (Status != WorkItemStatus.Approved || CurrentPlanId != planId || Revision != workItemRevision)
        {
            throw new InvalidOperationException("Only the approved current plan can be executed.");
        }

        Status = WorkItemStatus.Executing;
        UpdatedAt = now;
    }

    public void CompleteExecution(
        string summary,
        IReadOnlyList<string> outputs,
        string executionInvocationId,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionInvocationId);
        if (Status != WorkItemStatus.Executing)
        {
            throw new InvalidOperationException("The work item is not executing.");
        }

        Status = WorkItemStatus.Completed;
        Completion = new WorkItemCompletion
        {
            Summary = summary.Trim(),
            Outputs = outputs
                .Where(output => !string.IsNullOrWhiteSpace(output))
                .Select(output => output.Trim())
                .ToList(),
            ExecutionInvocationId = executionInvocationId.Trim(),
            CompletedAt = now
        };
        CompletedAt = now;
        UpdatedAt = now;
    }

    public void MarkExecutionUncertain(DateTimeOffset now)
    {
        if (Status == WorkItemStatus.Executing)
        {
            Status = WorkItemStatus.OutcomeUnknown;
            UpdatedAt = now;
        }
    }

    public void PrepareExecutionRetry(DateTimeOffset now)
    {
        if (Status != WorkItemStatus.OutcomeUnknown)
        {
            throw new InvalidOperationException("Only an execution with an unknown outcome can be retried.");
        }

        var plan = FindCurrentPlan();
        if (plan?.Review?.Kind != PlanReviewKind.Approved)
        {
            throw new InvalidOperationException("The approved plan for this execution could not be found.");
        }

        Status = WorkItemStatus.Approved;
        UpdatedAt = now;
    }

    public void MarkApprovedExecutionFailed(DateTimeOffset now)
    {
        if (Status == WorkItemStatus.Approved)
        {
            Status = WorkItemStatus.NeedsAttention;
            UpdatedAt = now;
        }
    }

    public void Discard(DateTimeOffset now, string source = "Web")
    {
        if (Status == WorkItemStatus.Executing)
        {
            throw new InvalidOperationException("An executing work item cannot be discarded because external effects may already be in progress.");
        }

        if (Status is WorkItemStatus.Completed or WorkItemStatus.Discarded)
        {
            return;
        }

        Status = WorkItemStatus.Discarded;
        CurrentPlanId = null;
        DiscardedAt = now;
        DiscardedFrom = source;
        UpdatedAt = now;
    }

    public void Restore(DateTimeOffset now, string source = "Web")
    {
        if (Status != WorkItemStatus.Discarded)
        {
            throw new InvalidOperationException("Only a discarded work item can be restored.");
        }

        Status = WorkItemStatus.Planning;
        Revision++;
        CurrentPlanId = null;
        LatestPlanningFeedback = null;
        Comments.Add(CreateComment(
            $"Restored from the discarded state through {source}.",
            CommentAuthorKind.System,
            WorkItemCommentKind.Context,
            now));
        UpdatedAt = now;
    }

    public void AddExecutionUpdate(string body, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        Comments.Add(CreateComment(
            body.Trim(),
            CommentAuthorKind.Samwise,
            WorkItemCommentKind.ExecutionUpdate,
            now));
        UpdatedAt = now;
    }

    public WorkItemPlan? FindCurrentPlan() => CurrentPlanId is null
        ? null
        : Plans.SingleOrDefault(plan => plan.Id == CurrentPlanId);

    public WorkItemPlan? FindLatestPlan() => Plans.Count == 0
        ? null
        : Plans.MaxBy(plan => plan.PlanNumber);

    private static WorkItemComment CreateComment(
        string body,
        CommentAuthorKind authorKind,
        WorkItemCommentKind kind,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            Body = body,
            AuthorKind = authorKind,
            Kind = kind,
            CreatedAt = now
        };

    private void SetPlanReview(
        string planId,
        PlanReviewKind kind,
        string? feedback,
        string source,
        DateTimeOffset now)
    {
        var plan = Plans.SingleOrDefault(candidate => candidate.Id == planId)
            ?? throw new InvalidOperationException("The current plan could not be found.");
        if (plan.Review is not null)
        {
            throw new InvalidOperationException("The plan has already been reviewed.");
        }

        plan.Review = new PlanReview
        {
            Kind = kind,
            Feedback = feedback,
            Source = source,
            ReviewedAt = now
        };
    }

    private void EnsureNotTerminal()
    {
        if (Status is WorkItemStatus.Completed or WorkItemStatus.Discarded)
        {
            throw new InvalidOperationException("A completed or discarded work item cannot be changed.");
        }
    }

    private void EnsureEditable()
    {
        EnsureNotTerminal();
        if (Status is WorkItemStatus.Executing or WorkItemStatus.OutcomeUnknown)
        {
            throw new InvalidOperationException("This work item cannot be changed because its external execution outcome is not safely repeatable.");
        }
    }
}

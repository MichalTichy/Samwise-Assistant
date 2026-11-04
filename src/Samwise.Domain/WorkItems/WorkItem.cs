using JasperFx;
using Samwise.Domain.WorkItems.Events;
using Shared.Infrastructure.Persistence;

namespace Samwise.Domain.WorkItems;

public sealed class WorkItem : IItemWithId, IRevisioned
{
    public const string AutomaticApprovalSource = "AutomaticApproval";
    // Facts recorded by transitions since the last dequeue. A private field, so it is never persisted.
    private readonly List<WorkItemEvent> pendingEvents = [];

    public string Id { get; set; } = string.Empty;
    public int Version { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public WorkItemStatus Status { get; set; }
    public int Revision { get; set; }
    public int NextPlanNumber { get; set; }
    public string? CurrentPlanId { get; set; }
    public string? LatestPlanningFeedback { get; set; }
    public string? WaitingReason { get; set; }
    public PlanningQuestion? PendingPlanningQuestion { get; set; }
    public List<PlanningDecision> PlanningDecisions { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public WorkItemCompletion? Completion { get; set; }
    public DateTimeOffset? DiscardedAt { get; set; }
    public string? DiscardedFrom { get; set; }
    public string? DiscardReason { get; set; }
    public string? DiscardInvocationId { get; set; }
    public List<WorkItemPlan> Plans { get; set; } = [];
    public List<WorkItemComment> Comments { get; set; } = [];
    public List<WorkItemSourceReference> Sources { get; set; } = [];

    public static WorkItem CreateFromInbound(
        string description,
        string? proposedTitle,
        DateTimeOffset now,
        WorkItemSourceReference? source = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        var normalized = description.Trim();
        var normalizedTitle = string.IsNullOrWhiteSpace(proposedTitle)
            ? normalized
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? normalized
            : proposedTitle.Trim();

        var workItem = new WorkItem
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
        if (source is not null)
        {
            workItem.Sources.Add(source);
        }

        workItem.Record(new WorkItemCreated(workItem.Id, workItem.Title, now));
        return workItem;
    }

    /// <summary>Returns and clears the facts recorded since the last call; publish them after persisting.</summary>
    public IReadOnlyList<WorkItemEvent> DequeueEvents()
    {
        var events = pendingEvents.ToList();
        pendingEvents.Clear();
        return events;
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
        PendingPlanningQuestion = null;
        Status = WorkItemStatus.AwaitingApproval;
        LatestPlanningFeedback = null;
        UpdatedAt = now;
        Record(new PlanProposed(Id, Title, now, plan.Id, plan.PlanNumber));
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
        Record(new PlanApproved(Id, Title, now, planId, source));
    }

    public void RecordAutomaticApprovalAssessment(
        string planId,
        int workItemRevision,
        AutomaticApprovalAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        if (Status != WorkItemStatus.AwaitingApproval || CurrentPlanId != planId || Revision != workItemRevision)
        {
            throw new InvalidOperationException("Only the current unreviewed plan can be assessed for automatic approval.");
        }

        var plan = FindCurrentPlan()!;
        if (plan.Review is not null || plan.AutomaticApproval is not null ||
            string.IsNullOrWhiteSpace(assessment.Reason) ||
            (assessment.Approved &&
             (string.IsNullOrWhiteSpace(assessment.ExecutionCatalogVersion) ||
              string.IsNullOrWhiteSpace(assessment.InvocationId))))
        {
            throw new InvalidOperationException("The automatic approval assessment is incomplete or already recorded.");
        }

        plan.AutomaticApproval = assessment;
        UpdatedAt = assessment.AssessedAt;
        if (assessment.Approved)
        {
            Approve(planId, workItemRevision, assessment.AssessedAt, AutomaticApprovalSource);
        }
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
        PendingPlanningQuestion = null;
        LatestPlanningFeedback = normalizedFeedback;
        Status = WorkItemStatus.Planning;
        UpdatedAt = now;
        Record(new PlanRejected(Id, Title, now, planId, normalizedFeedback, source));
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
        PendingPlanningQuestion = null;
        LatestPlanningFeedback = normalizedText;
        WaitingReason = null;
        Status = WorkItemStatus.Planning;
        UpdatedAt = now;
        Record(new WorkItemContextAdded(Id, Title, now, normalizedText, authorKind));
    }

    public void MarkPlanningFailed(DateTimeOffset now, string? reason = null)
    {
        if (Status == WorkItemStatus.Planning)
        {
            Status = WorkItemStatus.NeedsAttention;
            UpdatedAt = now;
            Record(new PlanningFailed(Id, Title, now, reason));
        }
    }

    public void RequestUserInput(PlanningQuestion question, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(question);
        if (Status != WorkItemStatus.Planning)
        {
            throw new InvalidOperationException("Only a work item being planned can request user input.");
        }

        if (question.WorkItemRevision != Revision ||
            string.IsNullOrWhiteSpace(question.Id) ||
            string.IsNullOrWhiteSpace(question.Prompt) ||
            string.IsNullOrWhiteSpace(question.PlanningInvocationId) ||
            question.Options.Count is < 2 or > 6 ||
            question.Options.Any(option => string.IsNullOrWhiteSpace(option.Id) || string.IsNullOrWhiteSpace(option.Label)) ||
            question.Options.Select(option => option.Id).Distinct(StringComparer.Ordinal).Count() != question.Options.Count)
        {
            throw new InvalidOperationException("The planning question is incomplete or does not belong to the current revision.");
        }

        PendingPlanningQuestion = question;
        CurrentPlanId = null;
        Status = WorkItemStatus.AwaitingUserInput;
        UpdatedAt = now;
        Record(new PlanningQuestionAsked(Id, Title, now, question.Id, question.Prompt));
    }

    public void AddInboundContext(
        string text,
        WorkItemSourceReference source,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(source);
        AddContext(text, now, CommentAuthorKind.System);
        Sources.Add(source);
    }

    public void AnswerPlanningQuestion(
        string questionId,
        string? optionId,
        string? otherAnswer,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(questionId);
        if (Status != WorkItemStatus.AwaitingUserInput || PendingPlanningQuestion?.Id != questionId)
        {
            throw new InvalidOperationException("Only the current planning question can be answered.");
        }

        var normalizedOther = string.IsNullOrWhiteSpace(otherAnswer) ? null : otherAnswer.Trim();
        PlanningQuestionOption? option = null;
        if (!string.IsNullOrWhiteSpace(optionId))
        {
            option = PendingPlanningQuestion.Options.SingleOrDefault(candidate => candidate.Id == optionId.Trim())
                ?? throw new InvalidOperationException("The selected planning answer is not available.");
        }

        if ((option is null) == (normalizedOther is null))
        {
            throw new InvalidOperationException("Choose one suggested answer or provide another answer.");
        }

        var answer = option?.Label ?? normalizedOther!;
        PlanningDecisions.Add(new PlanningDecision
        {
            Id = Guid.NewGuid().ToString(),
            QuestionId = PendingPlanningQuestion.Id,
            Question = PendingPlanningQuestion.Prompt,
            OptionId = option?.Id,
            Answer = answer,
            IsOther = option is null,
            AnsweredAt = now
        });
        Comments.Add(CreateComment(
            $"Planning decision\n\nQuestion: {PendingPlanningQuestion.Prompt}\n\nAnswer: {answer}",
            CommentAuthorKind.User,
            WorkItemCommentKind.PlanningDecision,
            now));
        PendingPlanningQuestion = null;
        Revision++;
        CurrentPlanId = null;
        LatestPlanningFeedback = answer;
        Status = WorkItemStatus.Planning;
        UpdatedAt = now;
        Record(new PlanningQuestionAnswered(Id, Title, now, questionId, answer));
    }

    public void Replan(DateTimeOffset now)
    {
        if (Status is not (
            WorkItemStatus.AwaitingUserInput
            or WorkItemStatus.AwaitingExternalEvent
            or WorkItemStatus.AwaitingApproval
            or WorkItemStatus.NeedsAttention))
        {
            throw new InvalidOperationException(
                "Only a work item awaiting user input, an external event, approval, or needing attention can be replanned.");
        }

        CurrentPlanId = null;
        PendingPlanningQuestion = null;
        WaitingReason = null;
        Status = WorkItemStatus.Planning;
        UpdatedAt = now;
        Record(new ReplanRequested(Id, Title, now));
    }

    public void AwaitExternalEvent(string reason, string agentInvocationId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentInvocationId);
        if (Status is not (WorkItemStatus.Planning or WorkItemStatus.Executing))
        {
            throw new InvalidOperationException("Only planning or executing work can await an external event.");
        }

        WaitingReason = reason.Trim();
        PendingPlanningQuestion = null;
        Status = WorkItemStatus.AwaitingExternalEvent;
        UpdatedAt = now;
        AddExecutionUpdate($"Waiting for external event (agent invocation {agentInvocationId}): {WaitingReason}", now);
        Record(new WorkItemAwaitingExternalEvent(Id, Title, now, WaitingReason, agentInvocationId));
    }

    public void StartExecution(string planId, int workItemRevision, DateTimeOffset now)
    {
        if (Status != WorkItemStatus.Approved || CurrentPlanId != planId || Revision != workItemRevision)
        {
            throw new InvalidOperationException("Only the approved current plan can be executed.");
        }

        Status = WorkItemStatus.Executing;
        UpdatedAt = now;
        Record(new ExecutionStarted(Id, Title, now, planId));
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
            Kind = WorkItemCompletionKind.Execution,
            Summary = summary.Trim(),
            Outputs = outputs
                .Where(output => !string.IsNullOrWhiteSpace(output))
                .Select(output => output.Trim())
                .ToList(),
            AgentInvocationId = executionInvocationId.Trim(),
            CompletedAt = now
        };
        CompletedAt = now;
        UpdatedAt = now;
        Record(new ExecutionCompleted(Id, Title, now, Completion.Summary, WasAutomaticallyExecuted()));
    }

    public void MarkExecutionUncertain(DateTimeOffset now, string? reason = null)
    {
        if (Status == WorkItemStatus.Executing)
        {
            Status = WorkItemStatus.OutcomeUnknown;
            UpdatedAt = now;
            Record(new ExecutionOutcomeUnknown(Id, Title, now, reason));
        }
    }

    public void CompleteDuringPlanning(
        string reason,
        string planningInvocationId,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(planningInvocationId);
        if (Status != WorkItemStatus.Planning)
        {
            throw new InvalidOperationException("Only a work item being planned can be resolved by planning.");
        }

        Status = WorkItemStatus.Completed;
        CurrentPlanId = null;
        PendingPlanningQuestion = null;
        Completion = new WorkItemCompletion
        {
            Kind = WorkItemCompletionKind.PlanningResolution,
            Summary = reason.Trim(),
            AgentInvocationId = planningInvocationId.Trim(),
            CompletedAt = now
        };
        CompletedAt = now;
        UpdatedAt = now;
        Record(new WorkItemResolvedDuringPlanning(Id, Title, now, Completion.Summary));
    }

    public void DiscardDuringPlanning(
        string reason,
        string planningInvocationId,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(planningInvocationId);
        if (Status != WorkItemStatus.Planning)
        {
            throw new InvalidOperationException("Only a work item being planned can be discarded by planning.");
        }

        Discard(now, "Planning agent", reason.Trim(), planningInvocationId.Trim());
        Comments.Add(CreateComment(
            $"Discarded during planning: {DiscardReason}",
            CommentAuthorKind.System,
            WorkItemCommentKind.Context,
            now));
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
        Record(new ExecutionRetryRequested(Id, Title, now));
    }

    public void MarkExecutionFailed(DateTimeOffset now, string? reason = null)
    {
        if (Status is WorkItemStatus.Approved or WorkItemStatus.Executing)
        {
            Status = WorkItemStatus.NeedsAttention;
            UpdatedAt = now;
            Record(new ExecutionFailed(Id, Title, now, reason));
        }
    }

    public void Discard(DateTimeOffset now, string source = "Web", string? reason = null) =>
        Discard(now, source, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), null);

    private void Discard(DateTimeOffset now, string source, string? reason, string? invocationId)
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
        PendingPlanningQuestion = null;
        DiscardedAt = now;
        DiscardedFrom = source;
        DiscardReason = reason;
        DiscardInvocationId = invocationId;
        UpdatedAt = now;
        Record(new WorkItemDiscarded(Id, Title, now, source, reason));
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
        PendingPlanningQuestion = null;
        LatestPlanningFeedback = null;
        WaitingReason = null;
        Comments.Add(CreateComment(
            $"Restored from the discarded state through {source}.",
            CommentAuthorKind.System,
            WorkItemCommentKind.Context,
            now));
        UpdatedAt = now;
        Record(new WorkItemRestored(Id, Title, now, source));
    }

    public void ReopenCompleted(string reason, DateTimeOffset now, string source = "Web")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Status != WorkItemStatus.Completed)
        {
            throw new InvalidOperationException("Only a completed work item can be reopened.");
        }

        var normalizedReason = reason.Trim();
        var previousCompletion = Completion;
        var previousResult = previousCompletion is null
            ? "No structured completion was recorded."
            : $"Previous completion ({previousCompletion.CompletedAt:u}): {previousCompletion.Summary}" +
              (previousCompletion.Outputs.Count == 0
                  ? string.Empty
                  : $"\n\nPrevious outputs:\n- {string.Join("\n- ", previousCompletion.Outputs)}");

        Comments.Add(CreateComment(
            $"Reopened through {source}: {normalizedReason}\n\n{previousResult}",
            CommentAuthorKind.System,
            WorkItemCommentKind.Context,
            now));
        Status = WorkItemStatus.Planning;
        Revision++;
        CurrentPlanId = null;
        PendingPlanningQuestion = null;
        LatestPlanningFeedback = normalizedReason;
        Completion = null;
        CompletedAt = null;
        UpdatedAt = now;
        Record(new WorkItemReopened(Id, Title, now, source, normalizedReason));
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

    public bool WasAutomaticallyExecuted() =>
        Status == WorkItemStatus.Completed &&
        Completion?.Kind == WorkItemCompletionKind.Execution &&
        FindCurrentPlan()?.Review?.Source == AutomaticApprovalSource;

    public WorkItemPlan? FindLatestPlan() => Plans.Count == 0
        ? null
        : Plans.MaxBy(plan => plan.PlanNumber);

    private void Record(WorkItemEvent workItemEvent) => pendingEvents.Add(workItemEvent);

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

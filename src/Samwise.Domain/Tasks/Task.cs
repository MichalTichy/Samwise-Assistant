namespace Samwise.Domain.Tasks;

public sealed class Task
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public SamwiseTaskStatus Status { get; set; }
    public int Revision { get; set; }
    public int NextPlanNumber { get; set; }
    public Guid? CurrentPlanId { get; set; }
    public string? LatestPlanningFeedback { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public static Task CreateFromInbound(
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

        return new Task
        {
            Id = Guid.NewGuid(),
            Title = normalizedTitle.Length <= 96 ? normalizedTitle : normalizedTitle[..93] + "...",
            Description = normalized,
            Status = SamwiseTaskStatus.Planning,
            Revision = 1,
            NextPlanNumber = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public int ReservePlanNumber(DateTimeOffset now)
    {
        if (Status != SamwiseTaskStatus.Planning)
        {
            throw new InvalidOperationException("Only a task that is planning can reserve a plan number.");
        }

        UpdatedAt = now;
        return NextPlanNumber++;
    }

    public void ApplyPlan(TaskPlan plan, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.TaskId != Id || plan.TaskRevision != Revision)
        {
            throw new InvalidOperationException("The plan does not belong to the current task revision.");
        }

        CurrentPlanId = plan.Id;
        Status = SamwiseTaskStatus.AwaitingApproval;
        LatestPlanningFeedback = null;
        UpdatedAt = now;
    }

    public void Approve(Guid planId, int taskRevision, DateTimeOffset now)
    {
        if (Status != SamwiseTaskStatus.AwaitingApproval || CurrentPlanId != planId || Revision != taskRevision)
        {
            throw new InvalidOperationException("Only the current plan can be approved.");
        }

        Status = SamwiseTaskStatus.Approved;
        UpdatedAt = now;
    }

    public void Reject(Guid planId, int taskRevision, string feedback, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feedback);
        if (Status != SamwiseTaskStatus.AwaitingApproval || CurrentPlanId != planId || Revision != taskRevision)
        {
            throw new InvalidOperationException("Only the current plan can be rejected.");
        }

        Revision++;
        CurrentPlanId = null;
        LatestPlanningFeedback = feedback.Trim();
        Status = SamwiseTaskStatus.Planning;
        UpdatedAt = now;
    }

    public void AddContext(string text, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        EnsureEditable();
        Revision++;
        CurrentPlanId = null;
        LatestPlanningFeedback = text.Trim();
        Status = SamwiseTaskStatus.Planning;
        UpdatedAt = now;
    }

    public void MarkPlanningFailed(DateTimeOffset now)
    {
        if (Status == SamwiseTaskStatus.Planning)
        {
            Status = SamwiseTaskStatus.NeedsAttention;
            UpdatedAt = now;
        }
    }

    public void RetryPlanning(DateTimeOffset now)
    {
        if (Status != SamwiseTaskStatus.NeedsAttention)
        {
            throw new InvalidOperationException("Only a task needing attention can retry planning.");
        }

        CurrentPlanId = null;
        Status = SamwiseTaskStatus.Planning;
        UpdatedAt = now;
    }

    public void StartExecution(Guid planId, int taskRevision, DateTimeOffset now)
    {
        if (Status != SamwiseTaskStatus.Approved || CurrentPlanId != planId || Revision != taskRevision)
        {
            throw new InvalidOperationException("Only the approved current plan can be executed.");
        }

        Status = SamwiseTaskStatus.Executing;
        UpdatedAt = now;
    }

    public void CompleteExecution(DateTimeOffset now)
    {
        if (Status != SamwiseTaskStatus.Executing)
        {
            throw new InvalidOperationException("The task is not executing.");
        }

        Status = SamwiseTaskStatus.Completed;
        CompletedAt = now;
        UpdatedAt = now;
    }

    public void MarkExecutionUncertain(DateTimeOffset now)
    {
        if (Status == SamwiseTaskStatus.Executing)
        {
            Status = SamwiseTaskStatus.OutcomeUnknown;
            UpdatedAt = now;
        }
    }

    public void Discard(DateTimeOffset now)
    {
        if (Status == SamwiseTaskStatus.Executing)
        {
            throw new InvalidOperationException("An executing task cannot be discarded because external effects may already be in progress.");
        }

        if (Status is SamwiseTaskStatus.Completed or SamwiseTaskStatus.Discarded)
        {
            return;
        }

        Status = SamwiseTaskStatus.Discarded;
        CurrentPlanId = null;
        UpdatedAt = now;
    }

    private void EnsureNotTerminal()
    {
        if (Status is SamwiseTaskStatus.Completed or SamwiseTaskStatus.Discarded)
        {
            throw new InvalidOperationException("A completed or discarded task cannot be changed.");
        }
    }

    private void EnsureEditable()
    {
        EnsureNotTerminal();
        if (Status is SamwiseTaskStatus.Executing or SamwiseTaskStatus.OutcomeUnknown)
        {
            throw new InvalidOperationException("This task cannot be changed because its external execution outcome is not safely repeatable.");
        }
    }
}

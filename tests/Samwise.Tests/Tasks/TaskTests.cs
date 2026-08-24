using Samwise.Domain.Tasks;
using DomainTask = Samwise.Domain.Tasks.Task;

namespace Samwise.Tests.Tasks;

public sealed class TaskTests
{
    private static readonly DateTimeOffset InitialTime = new(2026, 8, 17, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CurrentPlanCanBeApprovedOnlyForItsExactTaskRevision()
    {
        var task = DomainTask.CreateFromInbound("Prepare the revised offer.", "Revised offer", InitialTime);
        var plan = CreatePlan(task, task.ReservePlanNumber(InitialTime.AddMinutes(1)), InitialTime.AddMinutes(2));

        task.ApplyPlan(plan, InitialTime.AddMinutes(3));

        Assert.Equal(SamwiseTaskStatus.AwaitingApproval, task.Status);
        Assert.Equal(plan.Id, task.CurrentPlanId);
        Assert.Throws<InvalidOperationException>(() =>
            task.Approve(plan.Id, task.Revision + 1, InitialTime.AddMinutes(4)));
        Assert.Throws<InvalidOperationException>(() =>
            task.Approve(Guid.NewGuid().ToString(), task.Revision, InitialTime.AddMinutes(4)));

        task.Approve(plan.Id, plan.TaskRevision, InitialTime.AddMinutes(5));

        Assert.Equal(SamwiseTaskStatus.Approved, task.Status);
        Assert.Equal(InitialTime.AddMinutes(5), task.UpdatedAt);
    }

    [Fact]
    public void NewContextInvalidatesTheCurrentPlan()
    {
        var task = DomainTask.CreateFromInbound("Prepare the revised offer.", null, InitialTime);
        var plan = CreatePlan(task, task.ReservePlanNumber(InitialTime.AddMinutes(1)), InitialTime.AddMinutes(2));
        task.ApplyPlan(plan, InitialTime.AddMinutes(3));

        task.AddContext("  Use the newly approved price list.  ", InitialTime.AddMinutes(4));

        Assert.Equal(2, task.Revision);
        Assert.Equal(SamwiseTaskStatus.Planning, task.Status);
        Assert.Null(task.CurrentPlanId);
        Assert.Equal("Use the newly approved price list.", task.LatestPlanningFeedback);
        Assert.Throws<InvalidOperationException>(() =>
            task.Approve(plan.Id, plan.TaskRevision, InitialTime.AddMinutes(5)));
    }

    [Fact]
    public void RejectingAPlanRecordsFeedbackAndStartsANewRevision()
    {
        var task = DomainTask.CreateFromInbound("Prepare the revised offer.", null, InitialTime);
        var plan = CreatePlan(task, task.ReservePlanNumber(InitialTime.AddMinutes(1)), InitialTime.AddMinutes(2));
        task.ApplyPlan(plan, InitialTime.AddMinutes(3));

        task.Reject(
            plan.Id,
            plan.TaskRevision,
            "  Include delivery dates in the plan.  ",
            InitialTime.AddMinutes(4));

        Assert.Equal(2, task.Revision);
        Assert.Equal(SamwiseTaskStatus.Planning, task.Status);
        Assert.Null(task.CurrentPlanId);
        Assert.Equal("Include delivery dates in the plan.", task.LatestPlanningFeedback);
        Assert.Equal(InitialTime.AddMinutes(4), task.UpdatedAt);
    }

    [Fact]
    public void ApplyingAPlanFromAnotherRevisionIsRejected()
    {
        var task = DomainTask.CreateFromInbound("Prepare the revised offer.", null, InitialTime);
        var stalePlan = CreatePlan(task, task.ReservePlanNumber(InitialTime.AddMinutes(1)), InitialTime.AddMinutes(2));
        task.AddContext("Use the new price list.", InitialTime.AddMinutes(3));

        Assert.Throws<InvalidOperationException>(() => task.ApplyPlan(stalePlan, InitialTime.AddMinutes(4)));
        Assert.Null(task.CurrentPlanId);
        Assert.Equal(SamwiseTaskStatus.Planning, task.Status);
    }

    [Fact]
    public void ExecutingTaskCannotBeChangedOrDiscarded()
    {
        var task = DomainTask.CreateFromInbound("Send the revised offer.", null, InitialTime);
        var plan = CreatePlan(task, task.ReservePlanNumber(InitialTime.AddMinutes(1)), InitialTime.AddMinutes(2));
        task.ApplyPlan(plan, InitialTime.AddMinutes(3));
        task.Approve(plan.Id, task.Revision, InitialTime.AddMinutes(4));
        task.StartExecution(plan.Id, task.Revision, InitialTime.AddMinutes(5));

        Assert.Throws<InvalidOperationException>(() => task.AddContext("Use the new price.", InitialTime.AddMinutes(6)));
        Assert.Throws<InvalidOperationException>(() => task.Discard(InitialTime.AddMinutes(6)));
    }

    [Fact]
    public void InterruptedExecutionIsNotRetryableOrEditable()
    {
        var task = DomainTask.CreateFromInbound("Send the revised offer.", null, InitialTime);
        var plan = CreatePlan(task, task.ReservePlanNumber(InitialTime.AddMinutes(1)), InitialTime.AddMinutes(2));
        task.ApplyPlan(plan, InitialTime.AddMinutes(3));
        task.Approve(plan.Id, task.Revision, InitialTime.AddMinutes(4));
        task.StartExecution(plan.Id, task.Revision, InitialTime.AddMinutes(5));

        task.MarkExecutionUncertain(InitialTime.AddMinutes(6));

        Assert.Equal(SamwiseTaskStatus.OutcomeUnknown, task.Status);
        Assert.Equal(plan.Id, task.CurrentPlanId);
        Assert.Throws<InvalidOperationException>(() => task.AddContext("Try again.", InitialTime.AddMinutes(7)));
        Assert.Throws<InvalidOperationException>(() => task.RetryPlanning(InitialTime.AddMinutes(7)));
    }

    [Fact]
    public void PlanningFailureCanBeRetriedAndInvalidatesItsPlan()
    {
        var task = DomainTask.CreateFromInbound("Prepare the revised offer.", null, InitialTime);
        var plan = CreatePlan(task, task.ReservePlanNumber(InitialTime.AddMinutes(1)), InitialTime.AddMinutes(2));
        task.ApplyPlan(plan, InitialTime.AddMinutes(3));
        task.Status = SamwiseTaskStatus.NeedsAttention;

        task.RetryPlanning(InitialTime.AddMinutes(4));

        Assert.Equal(SamwiseTaskStatus.Planning, task.Status);
        Assert.Null(task.CurrentPlanId);
    }

    private static TaskPlan CreatePlan(DomainTask task, int planNumber, DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            TaskId = task.Id,
            TaskRevision = task.Revision,
            PlanNumber = planNumber,
            Summary = "Prepare and verify the revised offer.",
            CreatedAt = createdAt
        };
}

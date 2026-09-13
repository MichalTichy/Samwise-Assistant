using Samwise.Domain.WorkItems;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;
using WorkItemStatus = Samwise.Domain.WorkItems.WorkItemStatus;

namespace Samwise.Tests.WorkItems;

public sealed class WorkItemTests
{
    private static readonly DateTimeOffset InitialTime = new(2026, 8, 17, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CurrentPlanCanBeApprovedOnlyForItsExactWorkItemRevision()
    {
        var workItem = DomainWorkItem.CreateFromInbound("Prepare the revised offer.", "Revised offer", InitialTime);
        var plan = CreatePlan(workItem, workItem.ReservePlanNumber(InitialTime.AddMinutes(1)), InitialTime.AddMinutes(2));

        workItem.ApplyPlan(plan, InitialTime.AddMinutes(3));

        Assert.Equal(WorkItemStatus.AwaitingApproval, workItem.Status);
        Assert.Equal(plan.Id, workItem.CurrentPlanId);
        Assert.Same(plan, Assert.Single(workItem.Plans));
        Assert.Throws<InvalidOperationException>(() =>
            workItem.Approve(plan.Id, workItem.Revision + 1, InitialTime.AddMinutes(4)));
        Assert.Throws<InvalidOperationException>(() =>
            workItem.Approve(Guid.NewGuid().ToString(), workItem.Revision, InitialTime.AddMinutes(4)));

        workItem.Approve(plan.Id, plan.WorkItemRevision, InitialTime.AddMinutes(5));

        Assert.Equal(WorkItemStatus.Approved, workItem.Status);
        Assert.Equal(InitialTime.AddMinutes(5), workItem.UpdatedAt);
        var review = Assert.IsType<PlanReview>(plan.Review);
        Assert.Equal(PlanReviewKind.Approved, review.Kind);
        Assert.Equal("Web", review.Source);
        Assert.Equal(InitialTime.AddMinutes(5), review.ReviewedAt);
    }

    [Fact]
    public void NewContextInvalidatesTheCurrentPlan()
    {
        var workItem = DomainWorkItem.CreateFromInbound("Prepare the revised offer.", null, InitialTime);
        var plan = CreatePlan(workItem, workItem.ReservePlanNumber(InitialTime.AddMinutes(1)), InitialTime.AddMinutes(2));
        workItem.ApplyPlan(plan, InitialTime.AddMinutes(3));

        workItem.AddContext("  Use the newly approved price list.  ", InitialTime.AddMinutes(4));

        Assert.Equal(2, workItem.Revision);
        Assert.Equal(WorkItemStatus.Planning, workItem.Status);
        Assert.Null(workItem.CurrentPlanId);
        Assert.Equal("Use the newly approved price list.", workItem.LatestPlanningFeedback);
        var comment = Assert.Single(workItem.Comments);
        Assert.Equal("Use the newly approved price list.", comment.Body);
        Assert.Equal(WorkItemCommentKind.Context, comment.Kind);
        Assert.Throws<InvalidOperationException>(() =>
            workItem.Approve(plan.Id, plan.WorkItemRevision, InitialTime.AddMinutes(5)));
    }

    [Fact]
    public void RejectingAPlanRecordsFeedbackAndStartsANewRevision()
    {
        var workItem = DomainWorkItem.CreateFromInbound("Prepare the revised offer.", null, InitialTime);
        var plan = CreatePlan(workItem, workItem.ReservePlanNumber(InitialTime.AddMinutes(1)), InitialTime.AddMinutes(2));
        workItem.ApplyPlan(plan, InitialTime.AddMinutes(3));

        workItem.Reject(
            plan.Id,
            plan.WorkItemRevision,
            "  Include delivery dates in the plan.  ",
            InitialTime.AddMinutes(4));

        Assert.Equal(2, workItem.Revision);
        Assert.Equal(WorkItemStatus.Planning, workItem.Status);
        Assert.Null(workItem.CurrentPlanId);
        Assert.Equal("Include delivery dates in the plan.", workItem.LatestPlanningFeedback);
        Assert.Equal(InitialTime.AddMinutes(4), workItem.UpdatedAt);
        var review = Assert.IsType<PlanReview>(plan.Review);
        Assert.Equal(PlanReviewKind.Rejected, review.Kind);
        Assert.Equal("Include delivery dates in the plan.", review.Feedback);
        Assert.Equal(WorkItemCommentKind.PlanningFeedback, Assert.Single(workItem.Comments).Kind);
    }

    [Fact]
    public void ApplyingAPlanFromAnotherRevisionIsRejected()
    {
        var workItem = DomainWorkItem.CreateFromInbound("Prepare the revised offer.", null, InitialTime);
        var stalePlan = CreatePlan(workItem, workItem.ReservePlanNumber(InitialTime.AddMinutes(1)), InitialTime.AddMinutes(2));
        workItem.AddContext("Use the new price list.", InitialTime.AddMinutes(3));

        Assert.Throws<InvalidOperationException>(() => workItem.ApplyPlan(stalePlan, InitialTime.AddMinutes(4)));
        Assert.Null(workItem.CurrentPlanId);
        Assert.Equal(WorkItemStatus.Planning, workItem.Status);
    }

    [Fact]
    public void ExecutingWorkItemCannotBeChangedOrDiscarded()
    {
        var workItem = DomainWorkItem.CreateFromInbound("Send the revised offer.", null, InitialTime);
        var plan = CreatePlan(workItem, workItem.ReservePlanNumber(InitialTime.AddMinutes(1)), InitialTime.AddMinutes(2));
        workItem.ApplyPlan(plan, InitialTime.AddMinutes(3));
        workItem.Approve(plan.Id, workItem.Revision, InitialTime.AddMinutes(4));
        workItem.StartExecution(plan.Id, workItem.Revision, InitialTime.AddMinutes(5));

        Assert.Throws<InvalidOperationException>(() => workItem.AddContext("Use the new price.", InitialTime.AddMinutes(6)));
        Assert.Throws<InvalidOperationException>(() => workItem.Discard(InitialTime.AddMinutes(6)));
    }

    [Fact]
    public void InterruptedExecutionIsNotRetryableOrEditable()
    {
        var workItem = DomainWorkItem.CreateFromInbound("Send the revised offer.", null, InitialTime);
        var plan = CreatePlan(workItem, workItem.ReservePlanNumber(InitialTime.AddMinutes(1)), InitialTime.AddMinutes(2));
        workItem.ApplyPlan(plan, InitialTime.AddMinutes(3));
        workItem.Approve(plan.Id, workItem.Revision, InitialTime.AddMinutes(4));
        workItem.StartExecution(plan.Id, workItem.Revision, InitialTime.AddMinutes(5));

        workItem.MarkExecutionUncertain(InitialTime.AddMinutes(6));

        Assert.Equal(WorkItemStatus.OutcomeUnknown, workItem.Status);
        Assert.Equal(plan.Id, workItem.CurrentPlanId);
        Assert.Throws<InvalidOperationException>(() => workItem.AddContext("Try again.", InitialTime.AddMinutes(7)));
        Assert.Throws<InvalidOperationException>(() => workItem.RetryPlanning(InitialTime.AddMinutes(7)));
    }

    [Fact]
    public void PlanningFailureCanBeRetriedAndInvalidatesItsPlan()
    {
        var workItem = DomainWorkItem.CreateFromInbound("Prepare the revised offer.", null, InitialTime);
        var plan = CreatePlan(workItem, workItem.ReservePlanNumber(InitialTime.AddMinutes(1)), InitialTime.AddMinutes(2));
        workItem.ApplyPlan(plan, InitialTime.AddMinutes(3));
        workItem.Status = WorkItemStatus.NeedsAttention;

        workItem.RetryPlanning(InitialTime.AddMinutes(4));

        Assert.Equal(WorkItemStatus.Planning, workItem.Status);
        Assert.Null(workItem.CurrentPlanId);
    }

    [Fact]
    public void DiscardAndCompletionMetadataAreRecordedInsideTheWorkItemDocument()
    {
        var discarded = DomainWorkItem.CreateFromInbound("Prepare the revised offer.", null, InitialTime);
        var discardedPlan = CreatePlan(
            discarded,
            discarded.ReservePlanNumber(InitialTime.AddMinutes(1)),
            InitialTime.AddMinutes(2));
        discarded.ApplyPlan(discardedPlan, InitialTime.AddMinutes(3));
        discarded.Discard(InitialTime.AddMinutes(4), "Todoist");

        Assert.Equal(WorkItemStatus.Discarded, discarded.Status);
        Assert.Equal(InitialTime.AddMinutes(4), discarded.DiscardedAt);
        Assert.Equal("Todoist", discarded.DiscardedFrom);
        Assert.Null(discardedPlan.Review);

        var executed = DomainWorkItem.CreateFromInbound("Send the revised offer.", null, InitialTime);
        var plan = CreatePlan(
            executed,
            executed.ReservePlanNumber(InitialTime.AddMinutes(1)),
            InitialTime.AddMinutes(2));
        executed.ApplyPlan(plan, InitialTime.AddMinutes(3));
        executed.Approve(plan.Id, executed.Revision, InitialTime.AddMinutes(4));
        executed.StartExecution(plan.Id, executed.Revision, InitialTime.AddMinutes(5));
        executed.CompleteExecution(
            "Created and sent the revised offer.",
            ["Created the offer document.", "Sent the offer to the customer."],
            "execution-invocation-id",
            InitialTime.AddMinutes(6));

        var completion = Assert.IsType<WorkItemCompletion>(executed.Completion);
        Assert.Equal("Created and sent the revised offer.", completion.Summary);
        Assert.Equal(2, completion.Outputs.Count);
        Assert.Equal("execution-invocation-id", completion.ExecutionInvocationId);
        Assert.Equal(InitialTime.AddMinutes(6), completion.CompletedAt);
        Assert.Empty(executed.Comments);
    }

    private static WorkItemPlan CreatePlan(DomainWorkItem workItem, int planNumber, DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            WorkItemId = workItem.Id,
            WorkItemRevision = workItem.Revision,
            PlanNumber = planNumber,
            Summary = "Prepare and verify the revised offer.",
            CreatedAt = createdAt
        };
}

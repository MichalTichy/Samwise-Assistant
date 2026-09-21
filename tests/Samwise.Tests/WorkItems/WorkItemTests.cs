using Samwise.Domain.WorkItems;
using Samwise.Domain.WorkItems.Events;
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
    public void InterruptedExecutionCanBeExplicitlyRetriedButNotEdited()
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
        Assert.Throws<InvalidOperationException>(() => workItem.Replan(InitialTime.AddMinutes(7)));

        workItem.PrepareExecutionRetry(InitialTime.AddMinutes(8));

        Assert.Equal(WorkItemStatus.Approved, workItem.Status);
        Assert.Equal(plan.Id, workItem.CurrentPlanId);
        Assert.Equal(InitialTime.AddMinutes(8), workItem.UpdatedAt);
        workItem.StartExecution(plan.Id, workItem.Revision, InitialTime.AddMinutes(9));
        Assert.Equal(WorkItemStatus.Executing, workItem.Status);
    }

    [Fact]
    public void PlanningQuestionAnswerBecomesStructuredContextForANewRevision()
    {
        var workItem = DomainWorkItem.CreateFromInbound("Schedule the proposed meeting.", null, InitialTime);
        var question = new PlanningQuestion
        {
            Id = "question-1",
            WorkItemRevision = workItem.Revision,
            PlanningAttemptNumber = workItem.ReservePlanNumber(InitialTime.AddMinutes(1)),
            Prompt = "How should the calendar conflict be handled?",
            Explanation = "Both offered times overlap another meeting.",
            Options =
            [
                new PlanningQuestionOption
                {
                    Id = "reschedule",
                    Label = "Use Tuesday and reschedule the conflict",
                    Description = "Move the existing meeting."
                },
                new PlanningQuestionOption
                {
                    Id = "alternatives",
                    Label = "Ask for alternative times"
                }
            ],
            PlanningInvocationId = "planning-invocation-1",
            CreatedAt = InitialTime.AddMinutes(2)
        };

        workItem.RequestUserInput(question, InitialTime.AddMinutes(2));
        workItem.AnswerPlanningQuestion(
            question.Id,
            "reschedule",
            null,
            InitialTime.AddMinutes(3));

        Assert.Equal(WorkItemStatus.Planning, workItem.Status);
        Assert.Equal(2, workItem.Revision);
        Assert.Null(workItem.PendingPlanningQuestion);
        var decision = Assert.Single(workItem.PlanningDecisions);
        Assert.Equal(question.Prompt, decision.Question);
        Assert.Equal("Use Tuesday and reschedule the conflict", decision.Answer);
        Assert.False(decision.IsOther);
        var comment = Assert.Single(workItem.Comments);
        Assert.Equal(WorkItemCommentKind.PlanningDecision, comment.Kind);
        Assert.Contains(decision.Answer, comment.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecutionRetryIsRejectedUnlessOutcomeIsUnknown()
    {
        var workItem = DomainWorkItem.CreateFromInbound("Send the revised offer.", null, InitialTime);

        var exception = Assert.Throws<InvalidOperationException>(
            () => workItem.PrepareExecutionRetry(InitialTime.AddMinutes(1)));

        Assert.Equal("Only an execution with an unknown outcome can be retried.", exception.Message);
    }

    [Fact]
    public void PlanningFailureCanBeRetriedAndInvalidatesItsPlan()
    {
        var workItem = DomainWorkItem.CreateFromInbound("Prepare the revised offer.", null, InitialTime);
        var plan = CreatePlan(workItem, workItem.ReservePlanNumber(InitialTime.AddMinutes(1)), InitialTime.AddMinutes(2));
        workItem.ApplyPlan(plan, InitialTime.AddMinutes(3));
        workItem.Status = WorkItemStatus.NeedsAttention;

        workItem.Replan(InitialTime.AddMinutes(4));

        Assert.Equal(WorkItemStatus.Planning, workItem.Status);
        Assert.Null(workItem.CurrentPlanId);
    }

    [Fact]
    public void AwaitingApprovalPlanCanBeReplannedAndRemainsInHistory()
    {
        var workItem = DomainWorkItem.CreateFromInbound("Prepare the revised offer.", null, InitialTime);
        var plan = CreatePlan(workItem, workItem.ReservePlanNumber(InitialTime.AddMinutes(1)), InitialTime.AddMinutes(2));
        workItem.ApplyPlan(plan, InitialTime.AddMinutes(3));

        workItem.Replan(InitialTime.AddMinutes(4));

        Assert.Equal(WorkItemStatus.Planning, workItem.Status);
        Assert.Null(workItem.CurrentPlanId);
        Assert.Same(plan, Assert.Single(workItem.Plans));
        Assert.Null(plan.Review);
        Assert.Equal(InitialTime.AddMinutes(4), workItem.UpdatedAt);
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
        Assert.Equal(WorkItemCompletionKind.Execution, completion.Kind);
        Assert.Equal("execution-invocation-id", completion.AgentInvocationId);
        Assert.Equal(InitialTime.AddMinutes(6), completion.CompletedAt);
        Assert.Empty(executed.Comments);
    }

    [Fact]
    public void DiscardedWorkItemCanBeRestoredForANewPlanningRevision()
    {
        var workItem = DomainWorkItem.CreateFromInbound("Add Saturnin to the calendar.", null, InitialTime);
        var plan = CreatePlan(
            workItem,
            workItem.ReservePlanNumber(InitialTime.AddMinutes(1)),
            InitialTime.AddMinutes(2));
        workItem.ApplyPlan(plan, InitialTime.AddMinutes(3));
        workItem.Discard(InitialTime.AddMinutes(4), "Web");

        workItem.Restore(InitialTime.AddMinutes(5), "MCP");

        Assert.Equal(WorkItemStatus.Planning, workItem.Status);
        Assert.Equal(2, workItem.Revision);
        Assert.Null(workItem.CurrentPlanId);
        Assert.Null(workItem.LatestPlanningFeedback);
        Assert.Same(plan, Assert.Single(workItem.Plans));
        Assert.Equal(InitialTime.AddMinutes(4), workItem.DiscardedAt);
        Assert.Equal("Web", workItem.DiscardedFrom);
        var comment = Assert.Single(workItem.Comments);
        Assert.Equal(CommentAuthorKind.System, comment.AuthorKind);
        Assert.Equal(WorkItemCommentKind.Context, comment.Kind);
        Assert.Equal("Restored from the discarded state through MCP.", comment.Body);
        Assert.Equal(InitialTime.AddMinutes(5), workItem.UpdatedAt);
    }

    [Fact]
    public void RestoreIsRejectedUnlessWorkItemIsDiscarded()
    {
        var workItem = DomainWorkItem.CreateFromInbound("Add Saturnin to the calendar.", null, InitialTime);

        var exception = Assert.Throws<InvalidOperationException>(
            () => workItem.Restore(InitialTime.AddMinutes(1)));

        Assert.Equal("Only a discarded work item can be restored.", exception.Message);
    }

    [Fact]
    public void PlanningCanCompleteAWorkItemWhenNewContextProvesItIsResolved()
    {
        var workItem = DomainWorkItem.CreateFromInbound("Reply to Rosie.", null, InitialTime);
        workItem.AddContext("Email sent to Rosie: I will be there.", InitialTime.AddMinutes(1));

        workItem.CompleteDuringPlanning(
            "The sent reply completed the requested response.",
            "planning-invocation-id",
            InitialTime.AddMinutes(2));

        Assert.Equal(WorkItemStatus.Completed, workItem.Status);
        Assert.Null(workItem.CurrentPlanId);
        var completion = Assert.IsType<WorkItemCompletion>(workItem.Completion);
        Assert.Equal(WorkItemCompletionKind.PlanningResolution, completion.Kind);
        Assert.Equal("planning-invocation-id", completion.AgentInvocationId);
        Assert.Equal("The sent reply completed the requested response.", completion.Summary);
    }

    [Fact]
    public void PlanningCanDiscardAWorkItemThatIsNoLongerRelevant()
    {
        var workItem = DomainWorkItem.CreateFromInbound("Arrange the optional meeting.", null, InitialTime);

        workItem.DiscardDuringPlanning(
            "The other participant cancelled the request.",
            "planning-invocation-id",
            InitialTime.AddMinutes(1));

        Assert.Equal(WorkItemStatus.Discarded, workItem.Status);
        Assert.Equal("Planning agent", workItem.DiscardedFrom);
        Assert.Equal("The other participant cancelled the request.", workItem.DiscardReason);
        Assert.Equal("planning-invocation-id", workItem.DiscardInvocationId);
        Assert.Contains("cancelled", Assert.Single(workItem.Comments).Body, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryTransitionRecordsItsFactOnce()
    {
        var workItem = DomainWorkItem.CreateFromInbound("Prepare the revised offer.", null, InitialTime);
        var plan = CreatePlan(workItem, workItem.ReservePlanNumber(InitialTime), InitialTime);
        workItem.ApplyPlan(plan, InitialTime.AddMinutes(1));
        workItem.Approve(plan.Id, workItem.Revision, InitialTime.AddMinutes(2));
        workItem.StartExecution(plan.Id, workItem.Revision, InitialTime.AddMinutes(3));
        workItem.CompleteExecution("Sent the offer.", ["Draft created"], "execution-invocation-id", InitialTime.AddMinutes(4));

        var events = workItem.DequeueEvents();

        Assert.Collection(
            events,
            created => Assert.IsType<WorkItemCreated>(created),
            proposed => Assert.Equal(plan.Id, Assert.IsType<PlanProposed>(proposed).PlanId),
            approved => Assert.Equal("Web", Assert.IsType<PlanApproved>(approved).Source),
            started => Assert.IsType<ExecutionStarted>(started),
            completed => Assert.Equal("Sent the offer.", Assert.IsType<ExecutionCompleted>(completed).Summary));
        Assert.All(events, recorded => Assert.Equal(workItem.Id, recorded.WorkItemId));
        Assert.Empty(workItem.DequeueEvents());
    }

    [Fact]
    public void PlanningDiscardRecordsItsReason()
    {
        var workItem = DomainWorkItem.CreateFromInbound("Arrange the optional meeting.", null, InitialTime);
        workItem.DequeueEvents();

        workItem.DiscardDuringPlanning("The request was cancelled.", "planning-invocation-id", InitialTime.AddMinutes(1));

        var discarded = Assert.IsType<WorkItemDiscarded>(Assert.Single(workItem.DequeueEvents()));
        Assert.Equal("Planning agent", discarded.Source);
        Assert.Equal("The request was cancelled.", discarded.Reason);
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

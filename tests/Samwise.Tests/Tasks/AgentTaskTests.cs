using Newtonsoft.Json.Linq;
using Samwise.Domain.Tasks;
using Samwise.Infrastructure.Persistence.Marten.Migrations;
using Samwise.Logic.Tasks;

namespace Samwise.Tests.Tasks;

public sealed class AgentTaskTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-17T09:00:00Z");

    [Fact]
    public void ChecklistRequiresEvidenceAndCompletionCannotScheduleAnotherRun()
    {
        var task = new AgentTask();
        TaskChecklistItem[] steps = [new() { Text = "Verify payment", IsCompleted = true }];
        Assert.Throws<ArgumentException>(() => task.Update("Payment", "Check exact order", steps, null, Now));
        steps[0].Evidence = "Wallet transaction 123, matched order reference and amount";
        Assert.Throws<ArgumentException>(() => task.Update("Payment", "Check exact order", steps, Now.AddDays(1), Now));
        task.Update("Payment", "Check exact order", steps, null, Now);
        Assert.Equal(Now, task.CompletedAt);
        Assert.Throws<InvalidOperationException>(() => task.StartRun("another-run", Now));
    }

    [Fact]
    public void RecurrencesCopyInstructionsAndResetChecklistWithoutSharingMutableState()
    {
        var routine = new AgentRoutine
        {
            Title = "Invoice", Instructions = "Use the month's hours", NextOccurrenceAt = Now,
            Checklist = [new() { Text = "Create invoice", IsCompleted = true, Evidence = "Previous month" }]
        };
        var first = routine.CreateOccurrence();
        routine.Instructions = "Updated future instructions";
        routine.Checklist[0].Text = "Verify hours first";
        routine.NextOccurrenceAt = Now.AddMonths(1);
        var second = routine.CreateOccurrence();
        Assert.Equal("Use the month's hours", first.Instructions);
        Assert.Equal("Create invoice", first.Checklist[0].Text);
        Assert.False(first.Checklist[0].IsCompleted);
        Assert.Null(first.Checklist[0].Evidence);
        first.Checklist[0].IsCompleted = true;
        Assert.False(second.Checklist[0].IsCompleted);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Theory]
    [InlineData("0 9 1W * *", "2026-10-31T12:00:00Z", "2026-11-02T08:00:00Z")]
    [InlineData("0 9 LW * *", "2026-10-01T00:00:00Z", "2026-10-30T08:00:00Z")]
    [InlineData("0 9 * * 1#2", "2026-11-17T09:00:00Z", "2026-12-14T08:00:00Z")]
    [InlineData("0 9 * * *", "2026-03-28T09:00:00Z", "2026-03-29T07:00:00Z")]
    public void RecurrencesUseLocalCalendarIncludingDst(string cron, string after, string expected) =>
        Assert.Equal(DateTimeOffset.Parse(expected), TaskRecurrence.Next(cron, "Europe/Prague", DateTimeOffset.Parse(after)));

    [Theory]
    [InlineData("Status", "WaitingReason", "Revision", "CurrentPlanId")]
    [InlineData("status", "waitingReason", "revision", "currentPlanId")]
    public void LegacyWaitingMigrationPreservesHistoryAndInvalidatesOldPlan(string status, string reason,
        string revision, string planId)
    {
        var data = new JObject
        {
            [status] = "AwaitingExternalEvent", [reason] = "Invoice 2884 created, approval missing",
            [revision] = 3, [planId] = "old-plan", ["Comments"] = new JArray("Invoice 2884 created")
        };
        RemoveExternalWaitingState.Rewrite(data);
        Assert.Equal("NeedsAttention", data[status]!.Value<string>());
        Assert.Equal(4, data[revision]!.Value<int>());
        Assert.Equal(JTokenType.Null, data[planId]!.Type);
        Assert.Null(data[reason]);
        Assert.Contains("2884", data.GetValue("LatestPlanningFeedback", StringComparison.OrdinalIgnoreCase)!.Value<string>());
        Assert.Equal("Invoice 2884 created", data["Comments"]![0]!.Value<string>());
    }

    [Fact]
    public void LegacyNumericStatusIsNormalizedForCurrentStringEnumQueries()
    {
        var data = new JObject { ["Status"] = 9, ["Revision"] = 1 };
        RemoveExternalWaitingState.Rewrite(data);
        Assert.Equal(JTokenType.String, data["Status"]!.Type);
        Assert.Equal("NeedsAttention", data["Status"]!.Value<string>());
    }
}

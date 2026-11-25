using JasperFx;
using Marten;
using Marten.Services;
using MediatR;
using Weasel.Core;
using Newtonsoft.Json;
using Samwise.Domain.Inbox;
using Samwise.Domain.Tasks;
using Samwise.Domain.WorkItems;
using Samwise.Infrastructure.Persistence.Marten.Storage;
using Samwise.TaskSources.Todoist.Configuration;
using Samwise.TaskSources.Todoist.Discovery;
using Samwise.TaskSources.Todoist.Legacy;
using Samwise.TaskSources.Todoist.Synchronization;
using Samwise.TaskSources.Todoist.Usecases.Dismissal;
using Samwise.Logic.WorkItems.Services;
using Samwise.Logic.Tasks.Usecases;
using Shared.Infrastructure.CurrentTenancyProvider;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Persistence.Marten.Repository.Document;
using Shared.Infrastructure.Persistence.Marten.SessionFactory;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Tests.TaskSources.Todoist;

public sealed class TodoistSynchronizationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-17T09:00:00Z");

    [Theory]
    [InlineData("missing", "unavailable")]
    [InlineData("completed", "completed")]
    [InlineData("deleted", "deleted")]
    [InlineData("undated", "undated")]
    public async Task ExactApiStateDoesNotTreatMissingAccessAsCompletion(string id, string expected)
    {
        await using var scope = await Scope.Create(); if (scope is null) return;
        using var client = new HttpClient(new StateHttpHandler()) { BaseAddress = new Uri("https://example.invalid/") };
        var reader = new Samwise.Infrastructure.Todoist.Readers.TodoistRestTaskReader(
            new Samwise.Infrastructure.Todoist.Api.TodoistApiClient(client), scope.Repo<TodoistTaskSourceConfiguration>(), new TestProtector());
        Assert.Equal(expected, (await reader.ReadTaskStateAsync(id)).State);
        await Assert.ThrowsAsync<Samwise.Infrastructure.Todoist.Api.TodoistApiException>(() => reader.ReadTaskStateAsync("forbidden"));
    }
    private sealed class TestProtector : Samwise.TaskSources.Todoist.Authentication.ITodoistCredentialProtector
    {
        public string Protect(string value) => value;
        public string Unprotect(string value) => value;
    }
    private sealed class StateHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var id = request.RequestUri!.Segments.Last();
            var status = id switch { "missing" => System.Net.HttpStatusCode.NotFound, "forbidden" => System.Net.HttpStatusCode.Unauthorized,
                _ => System.Net.HttpStatusCode.OK };
            var body = id switch { "completed" => "{\"checked\":true}", "deleted" => "{\"is_deleted\":true}", "undated" => "{\"id\":\"undated\",\"due\":null}", _ => "{}" };
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 3)]
    public async Task UndoAndPostponeIsNotProofOfNewRecurringOccurrence(bool recurring, int counter)
    {
        await using var scope = await Scope.Create(); if (scope is null) return;
        var source = Snapshot() with { Due = new() { Date = "2026-11-17T10:00:00Z", IsRecurring = recurring } };
        scope.Reader.Tasks = [source]; await scope.Sync();
        var task = Assert.Single(await scope.Repo<AgentTask>().ListAsync());
        task.Checklist.Add(new() { Text = "Prepared", IsCompleted = true, Evidence = "Draft 123" });
        await scope.Repo<AgentTask>().UpdateAsync(task);
        scope.Reader.Tasks = []; scope.Reader.State = new("completed"); await scope.Sync();
        scope.Reader.Tasks = [source with { CompletedCount = counter, Due = new() { Date = "2026-12-17T10:00:00Z", IsRecurring = recurring } }];
        Assert.Empty((await scope.Sync()).Errors);
        task = Assert.Single(await scope.Repo<AgentTask>().ListAsync());
        Assert.Equal(0, task.ExternalSource!.Generation); Assert.Equal("Draft 123", task.Checklist[1].Evidence);
    }

    [Fact]
    public async Task RestoredSourceCannotDiscardUnresolvedPartialFailure()
    {
        await using var scope = await Scope.Create(); if (scope is null) return;
        scope.Reader.Tasks = [Snapshot()]; await scope.Sync();
        var task = Assert.Single(await scope.Repo<AgentTask>().ListAsync());
        var failed = WorkItem.CreateFromInbound("Partial effects", "Invoice", Now);
        failed.Status = WorkItemStatus.NeedsAttention; failed.AgentTaskId = task.Id;
        failed.AddExecutionUpdate("Created invoice 2884; attachment failed", Now);
        task.WorkItemIds.Add(failed.Id); task.NextRunAt = null;
        await scope.Repo<WorkItem>().AddAsync(failed); await scope.Repo<AgentTask>().UpdateAsync(task);
        scope.Reader.Tasks = []; scope.Reader.State = new("unavailable"); await scope.Sync();
        scope.Reader.Tasks = [Snapshot()]; Assert.Empty((await scope.Sync()).Errors);
        task = Assert.Single(await scope.Repo<AgentTask>().ListAsync());
        Assert.True(task.ExternalSource!.IsActive); Assert.Null(task.NextRunAt);
        failed = await scope.Repo<WorkItem>().GetRequiredByIdAsync(failed.Id);
        Assert.Equal(WorkItemStatus.NeedsAttention, failed.Status);
        Assert.Contains(failed.Comments, comment => comment.Body.Contains("2884", StringComparison.Ordinal));
        Assert.Single(await scope.Repo<WorkItem>().ListAsync());
    }

    [Fact]
    public async Task SourceCounterGapKeepsBaselineAndNewGenerationsRetainUncertainOrigin()
    {
        await using var scope = await Scope.Create(); if (scope is null) return;
        scope.Reader.Tasks = [Snapshot()]; await scope.Sync();
        var first = Assert.Single(await scope.Repo<AgentTask>().ListAsync());
        var uncertain = WorkItem.CreateFromInbound("Attempt", "Invoice", Now);
        uncertain.Status = WorkItemStatus.OutcomeUnknown; uncertain.AgentTaskId = first.Id;
        first.WorkItemIds.Add(uncertain.Id); first.NextRunAt = null;
        await scope.Repo<WorkItem>().AddAsync(uncertain); await scope.Repo<AgentTask>().UpdateAsync(first);
        scope.Reader.Tasks = [Snapshot() with { CompletedCount = null }]; await scope.Sync();
        Assert.Equal(3, (await scope.Repo<AgentTask>().GetRequiredByIdAsync(first.Id)).ExternalSource!.CompletedCount);
        scope.Reader.Tasks = [Snapshot() with { CompletedCount = 4 }]; await scope.Sync();
        var second = (await scope.Repo<AgentTask>().ListAsync()).Single(item => item.ExternalSource!.Generation == 1);
        Assert.Equal(uncertain.Id, second.OriginWorkItemId);
        scope.Reader.Tasks = [Snapshot() with { CompletedCount = 2 }]; await scope.Sync();
        Assert.Equal(4, (await scope.Repo<AgentTask>().GetRequiredByIdAsync(second.Id)).ExternalSource!.CompletedCount);
        scope.Reader.Tasks = [Snapshot() with { CompletedCount = 5 }]; await scope.Sync();
        var third = (await scope.Repo<AgentTask>().ListAsync()).Single(item => item.ExternalSource!.Generation == 2);
        Assert.Equal(uncertain.Id, third.OriginWorkItemId);
        var scheduler = new ProcessScheduledTasksUsecaseHandler(scope.Repo<AgentRoutine>(), scope.Repo<AgentTask>(),
            scope.Repo<WorkItem>(), scope.Repo<InboundEvent>(), new Samwise.Logic.Inbox.Services.InboundEventProcessingQueue(),
            new Events(), new Notifications(), new Clock(), Microsoft.Extensions.Logging.Abstractions.NullLogger<ProcessScheduledTasksUsecaseHandler>.Instance, [new Gate(scope)]);
        third.NextRunAt = Now; await scope.Repo<AgentTask>().UpdateAsync(third);
        await scheduler.Handle(new(), default);
        Assert.Single(await scope.Repo<WorkItem>().ListAsync());
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("completed")]
    public async Task RestoreSameOccurrencePreservesVerifiedChecklist(string sourceState)
    {
        await using var scope = await Scope.Create(); if (scope is null) return;
        scope.Reader.Tasks = [Snapshot()]; await scope.Sync();
        var task = Assert.Single(await scope.Repo<AgentTask>().ListAsync());
        task.Checklist.Add(new() { Text = "Draft", IsCompleted = true, Evidence = "Draft 123" });
        await scope.Repo<AgentTask>().UpdateAsync(task);
        scope.Reader.Tasks = []; scope.Reader.State = new(sourceState); await scope.Sync();
        scope.Reader.Tasks = [Snapshot()]; Assert.Empty((await scope.Sync()).Errors);
        task = Assert.Single(await scope.Repo<AgentTask>().ListAsync());
        Assert.Equal(0, task.ExternalSource!.Generation); Assert.Equal("Draft 123", task.Checklist[1].Evidence);
    }

    [Fact]
    public async Task PostponingNewOverdueImportBeforeFirstDispatchMovesItsImplicitCheck()
    {
        await using var scope = await Scope.Create(); if (scope is null) return;
        scope.Reader.Tasks = [Snapshot() with { Due = new() { Date = "2026-11-16T09:00:00Z" } }];
        await scope.Sync();
        var task = Assert.Single(await scope.Repo<AgentTask>().ListAsync());
        Assert.Equal(Now, task.NextRunAt); Assert.Null(task.LastRunAt);
        scope.Reader.Tasks = [Snapshot() with { Due = new() { Date = "2026-11-20T09:00:00Z" } }];
        Assert.Empty((await scope.Sync()).Errors);
        task = Assert.Single(await scope.Repo<AgentTask>().ListAsync());
        Assert.Equal(Now.AddDays(3), task.NextRunAt); Assert.Empty(task.WorkItemIds);
    }

    [Fact]
    public async Task ChangesBeyondPromptExcerptAndLegacyNewCommentsWithdrawOldApproval()
    {
        await using var scope = await Scope.Create(); if (scope is null) return;
        var snapshot = Snapshot() with { Description = new string('a', 11_000) };
        scope.Reader.Tasks = [snapshot]; await scope.Sync();
        var task = Assert.Single(await scope.Repo<AgentTask>().ListAsync());
        var item = WorkItem.CreateFromInbound(task.Instructions, task.Title, Now);
        item.Status = WorkItemStatus.AwaitingApproval; item.CurrentPlanId = "old-approval"; item.AgentTaskId = task.Id;
        task.WorkItemIds.Add(item.Id); task.NextRunAt = null;
        await scope.Repo<WorkItem>().AddAsync(item); await scope.Repo<AgentTask>().UpdateAsync(task);
        scope.Reader.Tasks = [snapshot with { Description = snapshot.Description + "material change beyond excerpt" }];
        Assert.Empty((await scope.Sync()).Errors);
        Assert.Equal(WorkItemStatus.Discarded, (await scope.Repo<WorkItem>().GetRequiredByIdAsync(item.Id)).Status);

        var legacy = WorkItem.CreateFromInbound("Original legacy", "Legacy", Now);
        legacy.Status = WorkItemStatus.AwaitingApproval; legacy.CurrentPlanId = "legacy-approval";
        await scope.Repo<WorkItem>().AddAsync(legacy);
        snapshot = Snapshot("legacy");
        var input = new TodoistTaskInboundEvent { Id = "legacy-changed", TaskId = snapshot.TaskId, Due = snapshot.Due,
            DueAt = snapshot.Due.ResolveDueAt("UTC")!.Value, Content = snapshot.Content, Description = snapshot.Description,
            ReceivedAt = Now, ContextKey = TodoistTaskInboundEvent.CreateContextKey(snapshot.TaskId), DedupeKey = "legacy-changed" };
        input.Outcomes.Add(new() { Id = "outcome", WorkItemId = legacy.Id, Kind = InboundEventOutcomeKind.CreatedWorkItem });
        await scope.Repo<InboundEvent>().AddAsync(input);
        scope.Reader.Tasks = [snapshot];
        scope.Reader.Comments = [new("new-comment", Now, "Changed client instructions")];
        Assert.Empty((await scope.Sync()).Errors);
        Assert.Equal(WorkItemStatus.Discarded, (await scope.Repo<WorkItem>().GetRequiredByIdAsync(legacy.Id)).Status);
    }

    [Fact]
    public async Task CompletedTaskReviewsChangedInstructionsButPostponePreservesVerifiedEffects()
    {
        await using var scope = await Scope.Create(); if (scope is null) return;
        scope.Reader.Tasks = [Snapshot()]; await scope.Sync();
        var task = Assert.Single(await scope.Repo<AgentTask>().ListAsync());
        task.Update(task.Title, task.Instructions, [new() { Text = "Done", IsCompleted = true, Evidence = "Invoice 2884" }], null, Now);
        await scope.Repo<AgentTask>().UpdateAsync(task);
        scope.Reader.Tasks = [Snapshot() with { Due = new() { Date = "2026-11-24", IsRecurring = true } }];
        Assert.Empty((await scope.Sync()).Errors);
        task = Assert.Single(await scope.Repo<AgentTask>().ListAsync());
        Assert.NotNull(task.CompletedAt); Assert.Single(task.Checklist); Assert.Null(task.NextRunAt);
        scope.Reader.Tasks = [scope.Reader.Tasks[0] with { Description = "Additional requirement", DeadlineDate = new DateOnly(2026, 11, 25) }];
        Assert.Empty((await scope.Sync()).Errors);
        task = Assert.Single(await scope.Repo<AgentTask>().ListAsync());
        Assert.Null(task.CompletedAt); Assert.NotNull(task.NextRunAt);
        Assert.Equal("Invoice 2884", task.Checklist[0].Evidence);
        Assert.False(task.Checklist[1].IsCompleted); Assert.Contains("2026-11-25", task.Instructions);
        await scope.Sync();
        Assert.Equal(2, (await scope.Repo<AgentTask>().GetRequiredByIdAsync(task.Id)).Checklist.Count);
    }

    [Fact]
    public async Task LegacyAdoptionKeepsOlderLiveRunLastAndNeverOverwritesAnotherTaskLink()
    {
        await using var scope = await Scope.Create(); if (scope is null) return;
        var live = WorkItem.CreateFromInbound("Legacy", "Invoice", Now);
        live.Status = WorkItemStatus.AwaitingApproval;
        var completed = WorkItem.CreateFromInbound("Later intervention", "Done", Now.AddMinutes(1));
        completed.Status = WorkItemStatus.Completed;
        await scope.Repo<WorkItem>().AddAsync(live); await scope.Repo<WorkItem>().AddAsync(completed);
        foreach (var id in new[] { "1", "2" })
        {
            var snapshot = Snapshot(id);
            var input = new TodoistTaskInboundEvent { Id = "legacy-" + id, TaskId = id,
                Content = snapshot.Content, Description = snapshot.Description, Due = snapshot.Due,
                DueAt = snapshot.Due.ResolveDueAt("UTC")!.Value, ReceivedAt = Now,
                ContextKey = TodoistTaskInboundEvent.CreateContextKey(id), DedupeKey = "legacy-" + id };
            foreach (var workItem in new[] { live, completed })
                input.Outcomes.Add(new() { Id = Guid.NewGuid().ToString(), Kind = InboundEventOutcomeKind.CreatedWorkItem, WorkItemId = workItem.Id });
            await scope.Repo<InboundEvent>().AddAsync(input);
        }
        scope.Reader.Tasks = [Snapshot("1"), Snapshot("2")];
        var result = await scope.Sync();
        Assert.Equal(1, result.Imported); Assert.Single(result.Errors);
        var task = Assert.Single(await scope.Repo<AgentTask>().ListAsync());
        Assert.Equal(live.Id, task.WorkItemIds.Last());
        Assert.Equal(task.Id, (await scope.Repo<WorkItem>().GetRequiredByIdAsync(live.Id)).AgentTaskId);
        Assert.Equal("1", task.ExternalSource!.TaskId);
    }

    [Fact]
    public async Task LosingPreExecutionAttemptCannotOverwriteConcurrentExecution()
    {
        await using var scope = await Scope.Create(); if (scope is null) return;
        scope.Reader.Tasks = [Snapshot()]; await scope.Sync();
        var task = Assert.Single(await scope.Repo<AgentTask>().ListAsync());
        var item = WorkItem.CreateFromInbound(task.Instructions, task.Title, Now);
        item.AgentTaskId = task.Id; item.Status = WorkItemStatus.Approved; item.CurrentPlanId = "approved-plan";
        await scope.Repo<WorkItem>().AddAsync(item);
        var service = new WorkItemExecutionService(scope.Repo<WorkItem>(), null!, null!, null!, new Events(), new Clock(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkItemExecutionService>.Instance,
            scope.Repo<AgentTask>(), [new CompetingGate(scope, item.Id)]);
        await service.ExecuteAsync(new(item.Id, "approved-plan", item.Revision), default);
        Assert.Equal(WorkItemStatus.Executing, (await scope.Repo<WorkItem>().GetRequiredByIdAsync(item.Id)).Status);
    }
    private sealed class CompetingGate(Scope scope, string id) : Samwise.Logic.Tasks.IExternalTaskRunGate
    {
        public string Provider => "Todoist";
        public async Task<bool> RefreshBeforeRunAsync(string taskId, CancellationToken ct)
        {
            var item = await scope.Repo<WorkItem>().GetRequiredByIdAsync(id);
            item.StartExecution("approved-plan", item.Revision, Now);
            await scope.Repo<WorkItem>().UpdateAsync(item);
            return false; // The losing attempt fails after the other worker has claimed execution.
        }
    }

    [Fact]
    public async Task ExecutionRefreshWithdrawsChangedApprovalBeforeAnyRuntimeCall()
    {
        await using var scope = await Scope.Create(); if (scope is null) return;
        scope.Reader.Tasks = [Snapshot()]; await scope.Sync();
        var task = Assert.Single(await scope.Repo<AgentTask>().ListAsync());
        var item = WorkItem.CreateFromInbound(task.Instructions, task.Title, Now);
        item.AgentTaskId = task.Id; item.Status = WorkItemStatus.Approved; item.CurrentPlanId = "approved-plan";
        task.WorkItemIds.Add(item.Id); task.NextRunAt = null;
        await scope.Repo<WorkItem>().AddAsync(item); await scope.Repo<AgentTask>().UpdateAsync(task);
        scope.Reader.Tasks = [Snapshot() with { Content = "Changed after approval" }];
        // Null dependencies deliberately fail if execution gets past the source refresh.
        var service = new WorkItemExecutionService(scope.Repo<WorkItem>(), null!, null!, null!, new Events(), new Clock(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkItemExecutionService>.Instance,
            scope.Repo<AgentTask>(), [new Gate(scope)]);
        await service.ExecuteAsync(new(item.Id, "approved-plan", item.Revision), default);
        Assert.Equal(WorkItemStatus.Discarded, (await scope.Repo<WorkItem>().GetRequiredByIdAsync(item.Id)).Status);
    }
    private sealed class Gate(Scope scope) : Samwise.Logic.Tasks.IExternalTaskRunGate
    {
        public string Provider => "Todoist";
        public async Task<bool> RefreshBeforeRunAsync(string taskId, CancellationToken ct) => (await scope.Sync()).Errors.Count == 0;
    }

    [Fact]
    public async Task AllDatedOwnerTasksImportWithoutSelectionsAndExclusionsRetireLinkedTasks()
    {
        await using var scope = await Scope.Create();
        if (scope is null) return;
        scope.Reader.Tasks = [Snapshot("agent"), Snapshot("human") with { ProjectId = "personal" },
            Snapshot("other") with { ResponsibleUserId = "someone-else" }, Snapshot("label") with { ProjectId = "personal", Labels = ["samwise"] },
            Snapshot("old") with { Due = new() { Date = "2025-01-01" } }];
        var config = await scope.Repo<TodoistTaskSourceConfiguration>().GetRequiredByIdAsync(TodoistTaskSourceConfiguration.DocumentId);
        config.IncludedProjects.Clear(); config.IncludedLabels.Clear();
        await scope.Repo<TodoistTaskSourceConfiguration>().UpdateAsync(config);
        var result = await scope.Sync();
        Assert.Empty(result.Errors);
        Assert.Equal(4, result.Imported);
        Assert.Equal(1, result.Ignored);
        Assert.Equal(4, (await scope.Repo<AgentTask>().ListAsync()).Count);
        Assert.Empty(await scope.Repo<InboundEvent>().ListAsync());
        Assert.Empty(await scope.Repo<WorkItem>().ListAsync());
        config.IgnoredProjects = [new() { Id = "agent" }, new() { Id = "personal" }];
        await scope.Repo<TodoistTaskSourceConfiguration>().UpdateAsync(config);
        await scope.Sync();
        Assert.All(await scope.Repo<AgentTask>().ListAsync(), item => Assert.False(item.ExternalSource!.IsActive));
    }

    [Fact]
    public async Task UnchangedPollingPreservesAgentCheckAndApprovedVersionWhilePostponeKeepsChecklist()
    {
        await using var scope = await Scope.Create(); if (scope is null) return;
        scope.Reader.Tasks = [Snapshot()];
        Assert.Empty((await scope.Sync()).Errors);
        var task = Assert.Single(await scope.Repo<AgentTask>().ListAsync());
        task.NextRunAt = Now.AddDays(2);
        task.Checklist.Add(new() { Text = "Completed preparation", IsCompleted = true, Evidence = "Draft 123" });
        await scope.Repo<AgentTask>().UpdateAsync(task);
        task = await scope.Repo<AgentTask>().GetRequiredByIdAsync(task.Id);
        var version = task.Version;
        await scope.Sync();
        task = await scope.Repo<AgentTask>().GetRequiredByIdAsync(task.Id);
        Assert.Equal(version, task.Version); Assert.Equal(Now.AddDays(2), task.NextRunAt);
        scope.Reader.Tasks = [Snapshot() with { Due = new() { Date = "2026-11-24T10:00:00Z", IsRecurring = true } }];
        Assert.Empty((await scope.Sync()).Errors);
        task = Assert.Single(await scope.Repo<AgentTask>().ListAsync());
        Assert.Equal(Now.AddDays(2), task.NextRunAt);
        Assert.Equal("Draft 123", task.Checklist[1].Evidence);
        Assert.Equal(0, task.ExternalSource!.Generation);
    }

    [Fact]
    public async Task SourceEditWithdrawsPendingApprovalButDoesNotReplayOrEraseEvidence()
    {
        await using var scope = await Scope.Create(); if (scope is null) return;
        scope.Reader.Tasks = [Snapshot()]; await scope.Sync();
        var task = Assert.Single(await scope.Repo<AgentTask>().ListAsync());
        var item = WorkItem.CreateFromInbound(task.Instructions, task.Title, Now);
        item.AgentTaskId = task.Id; item.Status = WorkItemStatus.AwaitingApproval;
        task.WorkItemIds.Add(item.Id); task.NextRunAt = null;
        task.Checklist.Add(new() { Text = "Prepared", IsCompleted = true, Evidence = "Draft 123" });
        await scope.Repo<WorkItem>().AddAsync(item); await scope.Repo<AgentTask>().UpdateAsync(task);
        scope.Reader.Tasks = [Snapshot() with { Content = "Changed title" }];
        Assert.Empty((await scope.Sync()).Errors);
        Assert.Equal(WorkItemStatus.Discarded, (await scope.Repo<WorkItem>().GetRequiredByIdAsync(item.Id)).Status);
        task = Assert.Single(await scope.Repo<AgentTask>().ListAsync());
        Assert.Equal("Changed title", task.Title); Assert.Equal("Draft 123", task.Checklist[1].Evidence);
        var saver = new SaveAgentTaskUsecaseHandler(scope.Repo<AgentTask>(), scope.Repo<WorkItem>(), new Notifications(), new Clock(), new Events());
        await Assert.ThrowsAsync<InvalidOperationException>(() => saver.Handle(new(task.Id, task.Version, "Local title", task.Instructions, task.Checklist, Now.AddDays(1)), default));
    }

    [Fact]
    public async Task CompletionCounterCreatesIndependentOccurrenceWhile404PausesWithoutClaimingCompletion()
    {
        await using var scope = await Scope.Create(); if (scope is null) return;
        scope.Reader.Tasks = [Snapshot()]; await scope.Sync();
        var first = Assert.Single(await scope.Repo<AgentTask>().ListAsync());
        first.Checklist[0].IsCompleted = true; first.Checklist[0].Evidence = "Receipt 123";
        first.CompletedAt = Now; first.NextRunAt = null;
        await scope.Repo<AgentTask>().UpdateAsync(first);
        scope.Reader.Tasks = [Snapshot() with { CompletedCount = 4, Due = new() { Date = "2026-12-17", IsRecurring = true } }];
        Assert.Empty((await scope.Sync()).Errors);
        var tasks = await scope.Repo<AgentTask>().ListAsync(); Assert.Equal(2, tasks.Count);
        var next = Assert.Single(tasks, item => item.Id != first.Id);
        Assert.False(next.Checklist[0].IsCompleted); Assert.Null(next.Checklist[0].Evidence);
        Assert.Equal(1, next.ExternalSource!.Generation);
        scope.Reader.Tasks = []; scope.Reader.State = new("unavailable");
        Assert.Empty((await scope.Sync()).Errors);
        next = await scope.Repo<AgentTask>().GetRequiredByIdAsync(next.Id);
        Assert.False(next.ExternalSource!.IsActive); Assert.Equal("unavailable", next.ExternalSource.State);
        Assert.Null(next.CompletedAt); Assert.Null(next.NextRunAt);
        scope.Reader.Tasks = [Snapshot() with { CompletedCount = 4, Due = new() { Date = "2026-12-17", IsRecurring = true } }];
        await scope.Sync();
        Assert.Equal(2, (await scope.Repo<AgentTask>().ListAsync()).Count);
        Assert.True((await scope.Repo<AgentTask>().GetRequiredByIdAsync(next.Id)).ExternalSource!.IsActive);
    }

    [Fact]
    public async Task LegacyLiveWorkIsAdoptedAndOldPendingInputCannotEnterIntake()
    {
        await using var scope = await Scope.Create(); if (scope is null) return;
        var snapshot = Snapshot(); scope.Reader.Tasks = [snapshot];
        var item = WorkItem.CreateFromInbound("Existing source", snapshot.Content, Now);
        item.Status = WorkItemStatus.AwaitingApproval;
        await scope.Repo<WorkItem>().AddAsync(item);
        var input = new TodoistTaskInboundEvent { Id = "legacy-input", Due = snapshot.Due, DueAt = snapshot.Due.ResolveDueAt("UTC")!.Value,
            TaskId = snapshot.TaskId, Content = snapshot.Content, Description = snapshot.Description, ReceivedAt = Now,
            ContextKey = TodoistTaskInboundEvent.CreateContextKey(snapshot.TaskId), DedupeKey = "legacy" };
        input.Outcomes.Add(new() { Id = "outcome", Kind = InboundEventOutcomeKind.CreatedWorkItem, WorkItemId = item.Id });
        await scope.Repo<InboundEvent>().AddAsync(input);
        Assert.Empty((await scope.Sync()).Errors);
        var task = Assert.Single(await scope.Repo<AgentTask>().ListAsync());
        Assert.Equal(item.Id, Assert.Single(task.WorkItemIds)); Assert.Null(task.NextRunAt);
        Assert.Equal(task.Id, (await scope.Repo<WorkItem>().GetRequiredByIdAsync(item.Id)).AgentTaskId);
        var processor = new Samwise.Logic.Inbox.Services.InboundEventProcessingService(new RejectIntake(), new RejectPlanning(), scope.Repo<InboundEvent>());
        await processor.ProcessAsync(input.Id, default);
        Assert.Equal(InboundEventStatus.Processed, (await scope.Repo<InboundEvent>().GetRequiredByIdAsync(input.Id)).Status);
        Assert.Single(await scope.Repo<WorkItem>().ListAsync());
    }

    [Fact]
    public async Task DismissedRecurringTaskIsNotReimportedForAnyOccurrenceUntilRestored()
    {
        await using var scope = await Scope.Create(); if (scope is null) return;
        scope.Reader.Tasks = [Snapshot()]; await scope.Sync();
        var task = Assert.Single(await scope.Repo<AgentTask>().ListAsync());
        var item = WorkItem.CreateFromInbound(task.Instructions, task.Title, Now);
        item.AgentTaskId = task.Id; item.Status = WorkItemStatus.AwaitingApproval; item.CurrentPlanId = "pending-plan";
        task.WorkItemIds.Add(item.Id); task.NextRunAt = null;
        task.Checklist.Add(new() { Text = "Prepared", IsCompleted = true, Evidence = "Draft 123" });
        await scope.Repo<WorkItem>().AddAsync(item); await scope.Repo<AgentTask>().UpdateAsync(task);

        var dismissal = await scope.Dismiss(task.Id);
        Assert.True(dismissal.Succeeded); Assert.Equal("1", dismissal.TodoistTaskId);
        Assert.Equal(WorkItemStatus.Discarded, (await scope.Repo<WorkItem>().GetRequiredByIdAsync(item.Id)).Status);
        task = await scope.Repo<AgentTask>().GetRequiredByIdAsync(task.Id);
        Assert.False(task.ExternalSource!.IsActive); Assert.Equal("dismissed", task.ExternalSource.State); Assert.Null(task.NextRunAt);
        var config = await scope.Repo<TodoistTaskSourceConfiguration>().GetRequiredByIdAsync(TodoistTaskSourceConfiguration.DocumentId);
        var dismissed = Assert.Single(config.DismissedTasks);
        Assert.Equal("1", dismissed.TaskId); Assert.Equal(task.Id, dismissed.AgentTaskId); Assert.Equal(Now, dismissed.DismissedAt);

        var unchanged = await scope.Sync();
        Assert.Empty(unchanged.Errors); Assert.Equal(0, unchanged.Imported); Assert.Equal(1, unchanged.Ignored);
        scope.Reader.Tasks = [Snapshot() with { CompletedCount = 4, Due = new() { Date = "2026-12-17T10:00:00Z", IsRecurring = true } }];
        var nextOccurrence = await scope.Sync();
        Assert.Empty(nextOccurrence.Errors); Assert.Equal(0, nextOccurrence.Imported);
        task = Assert.Single(await scope.Repo<AgentTask>().ListAsync());
        Assert.False(task.ExternalSource!.IsActive); Assert.Equal("dismissed", task.ExternalSource.State);
        Assert.Equal(0, task.ExternalSource.Generation); Assert.Equal("Draft 123", task.Checklist[1].Evidence);

        Assert.True((await scope.Restore("1")).Succeeded);
        Assert.Equal("restored", (await scope.Repo<AgentTask>().GetRequiredByIdAsync(task.Id)).ExternalSource!.State);
        Assert.False((await scope.Restore("1")).Succeeded);
        var reimport = await scope.Sync();
        Assert.Empty(reimport.Errors); Assert.Equal(1, reimport.Imported);
        var tasks = await scope.Repo<AgentTask>().ListAsync(); Assert.Equal(2, tasks.Count);
        var next = Assert.Single(tasks, candidate => candidate.ExternalSource!.Generation == 1);
        Assert.True(next.ExternalSource!.IsActive); Assert.Equal("active", next.ExternalSource.State);
        Assert.NotNull(next.NextRunAt); Assert.Null(next.Checklist[0].Evidence);
        Assert.Empty((await scope.Repo<TodoistTaskSourceConfiguration>().GetRequiredByIdAsync(TodoistTaskSourceConfiguration.DocumentId)).DismissedTasks);
    }

    [Fact]
    public async Task DismissalNeverInterruptsStartedExecutionAndRestoreReactivatesSameOccurrence()
    {
        await using var scope = await Scope.Create(); if (scope is null) return;
        scope.Reader.Tasks = [Snapshot()]; await scope.Sync();
        var task = Assert.Single(await scope.Repo<AgentTask>().ListAsync());
        var running = WorkItem.CreateFromInbound(task.Instructions, task.Title, Now);
        running.AgentTaskId = task.Id; running.Status = WorkItemStatus.Executing;
        task.WorkItemIds.Add(running.Id); task.NextRunAt = null;
        await scope.Repo<WorkItem>().AddAsync(running); await scope.Repo<AgentTask>().UpdateAsync(task);

        // Opening an older generation still removes the Todoist task as a whole.
        Assert.True((await scope.Dismiss(task.Id)).Succeeded);
        Assert.Equal(WorkItemStatus.Executing, (await scope.Repo<WorkItem>().GetRequiredByIdAsync(running.Id)).Status);
        task = await scope.Repo<AgentTask>().GetRequiredByIdAsync(task.Id);
        Assert.False(task.ExternalSource!.IsActive); Assert.Equal("dismissed", task.ExternalSource.State);
        Assert.Empty((await scope.Sync()).Errors);
        Assert.Equal(WorkItemStatus.Executing, (await scope.Repo<WorkItem>().GetRequiredByIdAsync(running.Id)).Status);

        Assert.True((await scope.Restore("1")).Succeeded);
        Assert.Empty((await scope.Sync()).Errors);
        task = Assert.Single(await scope.Repo<AgentTask>().ListAsync());
        Assert.True(task.ExternalSource!.IsActive); Assert.Equal("active", task.ExternalSource.State);
        Assert.Equal(0, task.ExternalSource.Generation);
    }

    [Fact]
    public async Task OnlyImportedTodoistTasksCanBeDismissed()
    {
        await using var scope = await Scope.Create(); if (scope is null) return;
        var internalTask = new AgentTask { Id = "internal" };
        internalTask.Update("Internal", "Internal", [new() { Text = "Internal" }], Now.AddDays(1), Now);
        await scope.Repo<AgentTask>().AddAsync(internalTask);
        Assert.False((await scope.Dismiss("internal")).Succeeded);
        Assert.False((await scope.Dismiss("missing")).Succeeded);
        Assert.Empty((await scope.Repo<TodoistTaskSourceConfiguration>().GetRequiredByIdAsync(TodoistTaskSourceConfiguration.DocumentId)).DismissedTasks);
    }

    private static TodoistTaskSnapshot Snapshot(string id = "1") => new(id, "Check invoice", "Verify approval", "agent", "Agent", [], 1,
        new() { Date = "2026-11-17T10:00:00Z", IsRecurring = true }, null, "https://app.todoist.com/app/task/" + id, 3);

    private sealed class Scope(IDocumentStore store) : IAsyncDisposable
    {
        public Reader Reader { get; } = new();
        public IRepository<T> Repo<T>() where T : IItemWithId => new NoTenancyMartenRepository<T>(new NoTenancyByDefaultSessionFactory(store, new CurrentTenancyProviderNoTenancy()));
        public Task<TodoistSynchronizationResult> Sync() => new TodoistTaskSyncService(Repo<TodoistTaskSourceConfiguration>(), Repo<AgentTask>(), Repo<WorkItem>(),
            Repo<InboundEvent>(), Reader, Lock, new Events(), new Notifications(), new Clock()).SynchronizeAsync(null, default);
        public Task<TodoistTaskDismissalResult> Dismiss(string agentTaskId) => new DismissTodoistTaskUsecaseHandler(
            Repo<TodoistTaskSourceConfiguration>(), Repo<AgentTask>(), Repo<WorkItem>(), Lock, new Events(), new Notifications(), new Clock())
            .Handle(new(agentTaskId), default);
        public Task<TodoistTaskDismissalResult> Restore(string todoistTaskId) => new RestoreTodoistTaskUsecaseHandler(
            Repo<TodoistTaskSourceConfiguration>(), Repo<AgentTask>(), Lock, new Notifications(), new Clock())
            .Handle(new(todoistTaskId), default);
        private TodoistSynchronizationLock Lock { get; } = new();
        public static async Task<Scope?> Create()
        {
            var connection = Environment.GetEnvironmentVariable("SAMWISE_TEST_POSTGRES"); if (string.IsNullOrWhiteSpace(connection)) return null;
            var store = DocumentStore.For(options => {
                options.Connection(connection);
                var serializer = new JsonNetSerializer { EnumStorage = EnumStorage.AsString };
                serializer.Configure(settings => { settings.TypeNameHandling = TypeNameHandling.Objects;
                    settings.DateTimeZoneHandling = DateTimeZoneHandling.Unspecified;
                    settings.DateParseHandling = DateParseHandling.DateTimeOffset; }); options.Serializer(serializer);
                new SamwiseStorageConfiguration().Configure(options); options.DatabaseSchemaName = "todoisttest_" + Guid.NewGuid().ToString("N");
            });
            await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
            var scope = new Scope(store);
            await scope.Repo<TodoistTaskSourceConfiguration>().AddAsync(new TodoistTaskSourceConfiguration { ProtectedApiToken = "test-only", IncludedProjects = [new() { Id = "agent" }] });
            return scope;
        }
        public ValueTask DisposeAsync() => store.DisposeAsync();
    }
    private sealed class Reader : ITodoistTaskReader
    {
        public IReadOnlyList<TodoistTaskSnapshot> Tasks { get; set; } = [];
        public TodoistTaskState State { get; set; } = new("completed");
        public IReadOnlyList<TodoistCommentSnapshot> Comments { get; set; } = [];
        public Task<TodoistTaskReadResult> ReadDueTasksAsync(CancellationToken cancellationToken = default) => Task.FromResult(new TodoistTaskReadResult(Tasks, "UTC", "owner"));
        public Task<TodoistTaskState> ReadTaskStateAsync(string taskId, CancellationToken cancellationToken = default) => Task.FromResult(Tasks.FirstOrDefault(item => item.TaskId == taskId) is { } task ? new TodoistTaskState("active", task, "UTC", "owner") : State);
        public Task<IReadOnlyList<TodoistCommentSnapshot>> ReadCommentsAsync(string taskId, CancellationToken cancellationToken = default) => Task.FromResult(Comments);
        public Task<TodoistSourceCatalog> GetCatalogAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Events : IWorkItemEventPublisher { public Task PublishAsync(WorkItem item) => Task.CompletedTask; }
    private sealed class Notifications : IPublisher
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<T>(T notification, CancellationToken cancellationToken = default) where T : INotification => Task.CompletedTask;
    }
    private sealed class RejectIntake : Samwise.Logic.Inbox.Services.IInboundEventClassificationService
    { public Task<IReadOnlyList<string>> ClassifyAsync(string id, CancellationToken ct) => throw new InvalidOperationException("Legacy Todoist must not create Intake work"); }
    private sealed class RejectPlanning : IWorkItemPlanningService
    { public Task PlanAsync(string id, CancellationToken ct) => throw new InvalidOperationException("Legacy Todoist must not replay plans"); }
}

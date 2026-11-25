using MediatR;
using JasperFx;
using Weasel.Core;
using Marten;
using Marten.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Samwise.Domain.Inbox;
using Samwise.Domain.Tasks;
using Samwise.Domain.WorkItems;
using Samwise.Infrastructure.Persistence.Marten.Storage;
using Samwise.Infrastructure.Persistence.Marten.Migrations;
using Samwise.Logic.Inbox.Services;
using Samwise.Logic.Tasks.Usecases;
using Samwise.Logic.WorkItems.Services;
using Samwise.Web.Pages.Tasks;
using Shared.Infrastructure.CurrentTenancyProvider;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Persistence.Marten.Repository.Document;
using Shared.Infrastructure.Persistence.Marten.SessionFactory;
using Shared.Infrastructure.Persistence.Marten.UnitOfWorks;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Tests.Tasks;

public sealed class AgentTaskPersistenceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-17T09:00:00Z");

    [Theory]
    [InlineData(null)]
    [InlineData("not-running")]
    public async Task ExecutionTaskCreationRejectsMissingOrNonExecutingWorkItem(string? executingId)
    {
        if (CreateStore() is not { } store) return;
        await using var dispose = store;
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var workItems = Repository<WorkItem>(store);
        var item = WorkItem.CreateFromInbound("Not running", null, Now); item.Id = "not-running";
        await workItems.AddAsync(item);
        var saver = new SaveAgentTaskUsecaseHandler(Repository<AgentTask>(store), workItems, new NoNotifications(), new Clock(Now), new NoEvents());
        await Assert.ThrowsAsync<InvalidOperationException>(() => saver.Handle(new(null, 0, "Check", "Verify result",
            [new() { Text = "Verify" }], Now.AddDays(1), IsOwnerEdit: false, ExecutingWorkItemId: executingId), default));
        Assert.Empty(await Repository<AgentTask>(store).ListAsync());
    }

    [Fact]
    public async Task ExecutionTaskCreationAlwaysInheritsItsExecutingOrigin()
    {
        if (CreateStore() is not { } store) return;
        await using var dispose = store;
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var workItems = Repository<WorkItem>(store);
        var executing = WorkItem.CreateFromInbound("Approved work", null, Now); executing.Status = WorkItemStatus.Executing;
        var unrelated = WorkItem.CreateFromInbound("Other", null, Now); unrelated.Status = WorkItemStatus.Completed;
        await workItems.AddAsync(executing); await workItems.AddAsync(unrelated);
        var saver = new SaveAgentTaskUsecaseHandler(Repository<AgentTask>(store), workItems, new NoNotifications(), new Clock(Now), new NoEvents());
        var task = await saver.Handle(new(null, 0, "Check", "Verify result", [new() { Text = "Verify" }], Now.AddDays(1),
            OriginWorkItemId: unrelated.Id, IsOwnerEdit: false, ExecutingWorkItemId: executing.Id), default);
        Assert.Equal(executing.Id, task.OriginWorkItemId);
    }

    [Fact]
    public async Task ChangingRecurrenceMovesCursorWithoutOverwritingExistingOccurrenceAndDisableStopsDispatch()
    {
        if (CreateStore() is not { } store) return;
        await using var dispose = store;
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var tasks = Repository<AgentTask>(store);
        var routines = Repository<AgentRoutine>(store);
        var saver = new SaveAgentRoutineUsecaseHandler(routines, tasks, new NoNotifications(), new Clock(Now));
        var routine = await saver.Handle(new(null, 0, "Monthly", "Original instructions", [new() { Text = "Original outcome" }],
            "0 9 1 * *", "Europe/Prague", true), default);
        var original = Assert.Single(await tasks.ListAsync());
        routine = await routines.GetRequiredByIdAsync(routine.Id);
        routine = await saver.Handle(new(routine.Id, routine.Version, "Daily", "Updated instructions", [new() { Text = "New outcome" }],
            "0 9 * * *", "Europe/Prague", true), default);
        Assert.Equal(DateTimeOffset.Parse("2026-11-18T08:00:00Z"), routine.NextOccurrenceAt);
        Assert.Equal("Original instructions", (await tasks.GetRequiredByIdAsync(original.Id)).Instructions);
        Assert.Equal(2, (await tasks.ListAsync()).Count);
        routine = await routines.GetRequiredByIdAsync(routine.Id);
        await saver.Handle(new(routine.Id, routine.Version, routine.Title, routine.Instructions, routine.Checklist,
            routine.Cron, routine.TimeZoneId, false), default);
        await Handler(store, new InboundEventProcessingQueue(), DateTimeOffset.Parse("2026-11-18T09:00:00Z")).Handle(new(), default);
        Assert.Empty(await Repository<WorkItem>(store).ListAsync());
    }

    [Fact]
    public async Task StartupMakesInterruptedExecutionReconcileableWithoutRepeatingEffects()
    {
        if (CreateStore() is not { } store) return;
        await using var dispose = store;
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var item = WorkItem.CreateFromInbound("Invoice", null, Now);
        item.Status = WorkItemStatus.Executing;
        item.AddExecutionUpdate("Invoice 2884 created", Now);
        await Repository<WorkItem>(store).AddAsync(item);
        var invocation = new Samwise.Domain.Agents.AgentInvocation { Id = "interrupted", WorkItemId = item.Id,
            Profile = "Execution", Status = Samwise.Domain.Agents.AgentInvocationStatus.Running, StartedAt = Now.AddMinutes(-5) };
        await Repository<Samwise.Domain.Agents.AgentInvocation>(store).AddAsync(invocation);
        await new Samwise.Logic.WorkItems.Initialization.InterruptedExecutionInitializer(Repository<WorkItem>(store),
            Repository<Samwise.Domain.Agents.AgentInvocation>(store), new Clock(Now), new NoEvents())
            .InitializeAsync(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        item = await Repository<WorkItem>(store).GetRequiredByIdAsync(item.Id);
        Assert.Equal(WorkItemStatus.OutcomeUnknown, item.Status);
        Assert.Contains(item.Comments, comment => comment.Body.Contains("2884", StringComparison.Ordinal));
        Assert.Equal(Samwise.Domain.Agents.AgentInvocationStatus.OutcomeUnknown,
            (await Repository<Samwise.Domain.Agents.AgentInvocation>(store).GetRequiredByIdAsync(invocation.Id)).Status);
        item.Discard(Now, "Owner", "Reconciled invoice 2884; no replay");
        Assert.Equal(WorkItemStatus.Discarded, item.Status);
    }

    [Fact]
    public async Task DueTaskIsAtomicallyDispatchedOnceAndInputCanResumeAfterRestart()
    {
        if (CreateStore() is not { } store) return;
        await using var dispose = store;
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var tasks = Repository<AgentTask>(store);
        var workItems = Repository<WorkItem>(store);
        var inputs = Repository<InboundEvent>(store);
        var task = new AgentTask();
        task.Update("Check payment", "Find exact Wallet transaction", [new() { Text = "Verify transaction" }], Now, Now);
        await tasks.AddAsync(task);
        var queue = new InboundEventProcessingQueue();
        var handler = Handler(store, queue);
        await handler.Handle(new ProcessScheduledTasksUsecase(), CancellationToken.None);
        await handler.Handle(new ProcessScheduledTasksUsecase(), CancellationToken.None);

        var saved = await tasks.GetRequiredByIdAsync(task.Id);
        Assert.Null(saved.NextRunAt);
        var workItemId = Assert.Single(saved.WorkItemIds);
        Assert.Equal(task.Id, (await workItems.GetRequiredByIdAsync(workItemId)).AgentTaskId);
        var input = Assert.IsType<ScheduledTaskInboundEvent>(Assert.Single(await inputs.ListAsync()));
        Assert.Equal(InboundEventStatus.Pending, input.Status);
        var planning = new RecordingPlanning();
        var processor = new InboundEventProcessingService(new NoIntake(), planning, inputs);
        await processor.ProcessAsync(input.Id, CancellationToken.None);
        await processor.ProcessAsync(input.Id, CancellationToken.None);
        Assert.Equal(workItemId, Assert.Single(planning.Calls));
        Assert.Equal(InboundEventStatus.Processed, (await inputs.GetRequiredByIdAsync(input.Id)).Status);

        var summary = Assert.Single(await tasks.ListAsync(new TaskSummariesSpecification(false)));
        Assert.Equal(1, summary.ItemCount);
        Assert.Equal(0, summary.CompletedCount);
    }

    [Fact]
    public async Task CalendarProjectionKeepsUtcInstantAndPragueOffset()
    {
        if (CreateStore() is not { } store) return;
        await using var dispose = store;
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var task = new AgentTask();
        task.Update("Timezone check", "Verify instant", [new() { Text = "Check" }], Now.AddDays(2), Now);
        await Repository<AgentTask>(store).AddAsync(task);
        var summary = Assert.Single(await Repository<AgentTask>(store).ListAsync(new TaskSummariesSpecification(false)));
        Assert.Equal(Now.AddDays(2), summary.NextRunAt);
        Assert.Equal(10, TimeZoneInfo.ConvertTimeBySystemTimeZoneId(summary.NextRunAt!.Value, "Europe/Prague").Hour);
        Assert.Equal(Now.AddDays(2), (await Repository<AgentTask>(store).GetRequiredByIdAsync(task.Id)).NextRunAt);
    }

    [Theory]
    [InlineData(WorkItemStatus.Executing)]
    [InlineData(WorkItemStatus.NeedsAttention)]
    [InlineData(WorkItemStatus.OutcomeUnknown)]
    public async Task NewSelfCheckCannotRunBeforeItsOriginIsResolved(WorkItemStatus status)
    {
        if (CreateStore() is not { } store) return;
        await using var dispose = store;
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var origin = WorkItem.CreateFromInbound("Original operation", null, Now);
        origin.Status = status;
        await Repository<WorkItem>(store).AddAsync(origin);
        var task = new AgentTask { OriginWorkItemId = origin.Id };
        task.Update("Check outcome", "Verify payment", [new() { Text = "Find payment" }], Now, Now);
        await Repository<AgentTask>(store).AddAsync(task);
        await Handler(store, new InboundEventProcessingQueue()).Handle(new(), default);
        Assert.Empty(await Repository<InboundEvent>(store).ListAsync());
        Assert.Single(await Repository<WorkItem>(store).ListAsync());
    }

    [Theory]
    [InlineData(WorkItemStatus.AwaitingApproval)]
    [InlineData(WorkItemStatus.AwaitingUserInput)]
    [InlineData(WorkItemStatus.NeedsAttention)]
    [InlineData(WorkItemStatus.OutcomeUnknown)]
    public async Task FutureCheckDoesNotReplayUnfinishedOrUncertainWork(WorkItemStatus status)
    {
        if (CreateStore() is not { } store) return;
        await using var dispose = store;
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var previous = WorkItem.CreateFromInbound("Existing work", null, Now);
        previous.Status = status;
        await Repository<WorkItem>(store).AddAsync(previous);
        var task = new AgentTask { WorkItemIds = [previous.Id] };
        task.Update("Check", "Existing outcome", [new() { Text = "Check result" }], Now, Now);
        await Repository<AgentTask>(store).AddAsync(task);
        await Handler(store, new InboundEventProcessingQueue()).Handle(new ProcessScheduledTasksUsecase(), CancellationToken.None);
        Assert.Empty(await Repository<InboundEvent>(store).ListAsync());
        Assert.Single(await Repository<WorkItem>(store).ListAsync());
    }

    [Fact]
    public async Task RoutineCreatesOneFutureCopyAndDoesNotRequirePreviousOccurrenceToComplete()
    {
        if (CreateStore() is not { } store) return;
        await using var dispose = store;
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var tasks = Repository<AgentTask>(store);
        var routines = Repository<AgentRoutine>(store);
        var saver = new SaveAgentRoutineUsecaseHandler(routines, tasks, new NoNotifications(), new Clock(Now));
        var routine = await saver.Handle(new SaveAgentRoutineUsecase(null, 0, "Daily review", "Review inputs",
            [new() { Text = "Review" }], "0 10 * * *", "Europe/Prague", true), CancellationToken.None);
        var first = Assert.Single(await tasks.ListAsync());
        var firstTime = first.NextRunAt!.Value;
        await Handler(store, new InboundEventProcessingQueue(), firstTime).Handle(new ProcessScheduledTasksUsecase(), CancellationToken.None);
        var instances = await tasks.ListAsync();
        Assert.Equal(2, instances.Count);
        var next = Assert.Single(instances, item => item.Id != first.Id);
        Assert.Equal(firstTime.AddDays(1), next.NextRunAt);
        Assert.False(next.Checklist[0].IsCompleted);
        Assert.Null((await tasks.GetRequiredByIdAsync(first.Id)).CompletedAt);
        Assert.Single(await Repository<WorkItem>(store).ListAsync());

        var stale = await tasks.GetRequiredByIdAsync(next.Id);
        var current = await tasks.GetRequiredByIdAsync(next.Id);
        current.Title = "Updated instance";
        await tasks.UpdateAsync(current);
        await Assert.ThrowsAsync<JasperFx.ConcurrencyException>(() => tasks.UpdateAsync(stale));
        Assert.Equal("Daily review", (await routines.GetRequiredByIdAsync(routine.Id)).Title);
    }

    [Fact]
    public async Task EditingPendingOccurrenceWithdrawsOldApprovalWithoutChangingOtherInstances()
    {
        if (CreateStore() is not { } store) return;
        await using var dispose = store;
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var workItems = Repository<WorkItem>(store);
        var taskRepository = Repository<AgentTask>(store);
        var previous = WorkItem.CreateFromInbound("Old request", null, Now);
        previous.Status = WorkItemStatus.AwaitingApproval;
        previous.CurrentPlanId = "old-plan";
        await workItems.AddAsync(previous);
        var task = new AgentTask { WorkItemIds = [previous.Id] };
        task.Update("Check", "Original instructions", [new() { Text = "Verify" }], null, Now);
        await taskRepository.AddAsync(task);
        task = await taskRepository.GetRequiredByIdAsync(task.Id);
        var handler = new SaveAgentTaskUsecaseHandler(taskRepository, workItems, new NoNotifications(), new Clock(Now), new NoEvents());
        await handler.Handle(new SaveAgentTaskUsecase(task.Id, task.Version, "Changed check", "New instructions",
            task.Checklist, Now.AddDays(2), IsOwnerEdit: true), CancellationToken.None);
        previous = await workItems.GetRequiredByIdAsync(previous.Id);
        Assert.Equal(WorkItemStatus.Discarded, previous.Status);
        Assert.Null(previous.CurrentPlanId);
        Assert.Equal(Now.AddDays(2), (await taskRepository.GetRequiredByIdAsync(task.Id)).NextRunAt);
        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(new SaveAgentTaskUsecase(null, 0,
            "Undated", "Never run", [new() { Text = "Check" }], null), CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentTicksCreateExactlyOneRunPerTask()
    {
        if (CreateStore() is not { } store) return;
        await using var dispose = store;
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var tasks = Repository<AgentTask>(store);
        for (var index = 0; index < 2; index++)
        {
            var task = new AgentTask();
            task.Update("Check", "Read only", [new() { Text = "Verify" }], Now, Now);
            await tasks.AddAsync(task);
        }
        await Task.WhenAll(
            Handler(store, new InboundEventProcessingQueue()).Handle(new ProcessScheduledTasksUsecase(), CancellationToken.None),
            Handler(store, new InboundEventProcessingQueue()).Handle(new ProcessScheduledTasksUsecase(), CancellationToken.None));
        Assert.Equal(2, await Repository<WorkItem>(store).CountAsync());
        Assert.Equal(2, await Repository<InboundEvent>(store).CountAsync());
        Assert.All(await tasks.ListAsync(), task => Assert.Single(task.WorkItemIds));
    }

    [Fact]
    public async Task ExecutionProgressKeepsItsOwnRunAndCannotMutateAnotherPendingTask()
    {
        if (CreateStore() is not { } store) return;
        await using var dispose = store;
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var tasks = Repository<AgentTask>(store);
        var workItems = Repository<WorkItem>(store);
        var task = new AgentTask();
        task.Update("Check", "Verify payment", [new() { Text = "Find transaction" }], Now, Now);
        var workItem = WorkItem.CreateFromInbound(task.Instructions, task.Title, Now);
        workItem.AgentTaskId = task.Id;
        workItem.Status = WorkItemStatus.Executing;
        task.StartRun(workItem.Id, Now);
        await tasks.AddAsync(task);
        await workItems.AddAsync(workItem);
        var saver = new SaveAgentTaskUsecaseHandler(tasks, workItems, new NoNotifications(), new Clock(Now), new NoEvents());
        task = await tasks.GetRequiredByIdAsync(task.Id);
        await saver.Handle(new SaveAgentTaskUsecase(task.Id, task.Version, task.Title, task.Instructions,
            task.Checklist, Now.AddDays(1), IsOwnerEdit: false, ExecutingWorkItemId: workItem.Id), CancellationToken.None);
        Assert.Equal(WorkItemStatus.Executing, (await workItems.GetRequiredByIdAsync(workItem.Id)).Status);
        var unrelated = WorkItem.CreateFromInbound("Another run", null, Now);
        unrelated.Status = WorkItemStatus.Executing;
        await workItems.AddAsync(unrelated);
        task = await tasks.GetRequiredByIdAsync(task.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => saver.Handle(new SaveAgentTaskUsecase(
            task.Id, task.Version, task.Title, task.Instructions, task.Checklist, Now.AddDays(2),
            IsOwnerEdit: false, ExecutingWorkItemId: unrelated.Id), CancellationToken.None));
    }

    [Fact]
    public async Task StartupMigrationCanLoadRawLegacyEnumAndLeavesItsEvidenceReadable()
    {
        if (CreateStore(isolated: false) is not { } store) return;
        await using var dispose = store;
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var repository = Repository<WorkItem>(store);
        var original = WorkItem.CreateFromInbound("Invoice cycle", null, Now);
        original.AddExecutionUpdate("Invoice 2884 was created", Now);
        await repository.AddAsync(original);
        await using (var session = store.LightweightSession())
        {
            session.QueueSqlCommand("update samwise.mt_doc_workitem set data = data || ?::jsonb where id = ?",
                "{\"Status\":\"AwaitingExternalEvent\",\"WaitingReason\":\"Invoice 2884 awaits approval\",\"CurrentPlanId\":\"old-plan\"}", original.Id);
            await session.SaveChangesAsync();
        }
        await using (var session = store.LightweightSession())
        {
            await new RemoveExternalWaitingState().MigrateAsync(session, CancellationToken.None);
            await session.SaveChangesAsync();
        }
        var migrated = await repository.GetRequiredByIdAsync(original.Id);
        Assert.Equal(WorkItemStatus.NeedsAttention, migrated.Status);
        Assert.Null(migrated.CurrentPlanId);
        Assert.Contains("2884", migrated.LatestPlanningFeedback);
        Assert.Contains(migrated.Comments, comment => comment.Body.Contains("2884", StringComparison.Ordinal));
        await repository.DeleteByIdAsync(original.Id);
    }

    [Fact]
    public async Task ImportedHumanTaskRemainsInItsSourceDueMonthAfterAssessmentEnds()
    {
        if (CreateStore() is not { } store) return;
        await using var dispose = store;
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var due = Now.AddMonths(-3);
        var task = new AgentTask { Title = "Collect parcel", Instructions = "Collection remains the owner's action",
            Checklist = [new() { Text = "Collected" }], OccurrenceAt = Now, LastRunAt = Now,
            ExternalSource = new() { Provider = "Todoist", TaskId = "parcel", DueAt = due } };
        await Repository<AgentTask>(store).AddAsync(task);

        var summary = Assert.Single(await Repository<AgentTask>(store).ListAsync(
            new TaskSummariesSpecification(false, due.AddDays(-1), due.AddDays(1))));

        Assert.Equal(task.Id, summary.Id);
        Assert.Equal(due, summary.SourceDueAt);
        Assert.Null(summary.NextRunAt);
        Assert.Null(summary.CompletedAt);
        Assert.Equal(0, summary.CompletedCount);
    }

    [Fact]
    public async Task TodoistRenameMigrationPreservesSavedConfigurationAndLegacyHistory()
    {
        if (CreateStore(isolated: false) is not { } store) return;
        await using var dispose = store;
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var config = new Samwise.TaskSources.Todoist.Configuration.TodoistTaskSourceConfiguration
        {
            ProtectedApiToken = "protected-original-token", LastSuccessfulCheckAt = Now,
            IgnoredProjects = [new() { Id = "private-project", DisplayName = "Private" }],
            IncludedLabels = [new() { Id = "legacy-selection" }]
        };
        var legacyEvent = new Samwise.TaskSources.Todoist.Legacy.TodoistTaskInboundEvent
        {
            Id = "todoist-rename-history", TaskId = "original-task", Content = "Original task",
            Due = new() { Date = "2026-11-17", IsRecurring = true },
            Comments = [new() { CommentId = "comment", Content = "Historical instructions" }],
            Status = InboundEventStatus.Processed,
            Outcomes = [new() { Id = "outcome", WorkItemId = "existing-work", Kind = InboundEventOutcomeKind.CreatedWorkItem }]
        };
        var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Objects };
        var oldConfig = JsonConvert.SerializeObject(config, settings);
        foreach (var replacement in MigrateTodoistTaskSourceTypeOwnership.ConfigurationReplacements)
            oldConfig = oldConfig.Replace(replacement.NewName, replacement.OldName, StringComparison.Ordinal);
        var oldEvent = JsonConvert.SerializeObject(legacyEvent, settings);
        foreach (var replacement in MigrateTodoistTaskSourceTypeOwnership.LegacyEventReplacements)
            oldEvent = oldEvent.Replace(replacement.NewName, replacement.OldName, StringComparison.Ordinal);
        Assert.Contains("Samwise.Inputs.Todoist", oldConfig);
        Assert.Contains("Samwise.Inputs.Todoist", oldEvent);
        await using (var session = store.LightweightSession())
        {
            session.Store(config);
            session.Store<InboundEvent>(legacyEvent);
            await session.SaveChangesAsync();
            session.QueueSqlCommand("update samwise.mt_doc_todoistinputconfiguration set data = ?::jsonb, mt_dotnet_type = ? where id = ?",
                oldConfig, MigrateTodoistTaskSourceTypeOwnership.ConfigurationReplacements[0].OldName, config.Id);
            session.QueueSqlCommand("update samwise.mt_doc_inboundevent set data = ?::jsonb, mt_dotnet_type = ? where id = ?",
                oldEvent, MigrateTodoistTaskSourceTypeOwnership.LegacyEventReplacements[0].OldName, legacyEvent.Id);
            await session.SaveChangesAsync();
        }
        await using (var unit = new UnitOfWorkProvider())
        {
            // Match startup migrations: SQL participates in the shared unit-of-work transaction.
            var factory = new NoTenancyByDefaultSessionFactory(store, new CurrentTenancyProviderNoTenancy());
            await using var session = await factory.OpenSessionAsync(unit.Get());
            await new MigrateTodoistTaskSourceTypeOwnership().MigrateAsync(session, default);
            await new MigrateTodoistTaskSourceTypeOwnership().MigrateAsync(session, default); // Safe if repeated.
            await session.SaveChangesAsync();
            await unit.CommitAsync();
        }
        await using (var session = store.QuerySession())
        {
            var saved = await session.LoadAsync<Samwise.TaskSources.Todoist.Configuration.TodoistTaskSourceConfiguration>(config.Id);
            Assert.NotNull(saved);
            Assert.Equal(config.ProtectedApiToken, saved.ProtectedApiToken);
            Assert.Equal(Now, saved.LastSuccessfulCheckAt);
            Assert.Equal("private-project", Assert.Single(saved.IgnoredProjects).Id);
            Assert.Equal("legacy-selection", Assert.Single(saved.IncludedLabels).Id);
            var history = Assert.IsType<Samwise.TaskSources.Todoist.Legacy.TodoistTaskInboundEvent>(
                await session.LoadAsync<InboundEvent>(legacyEvent.Id));
            Assert.Equal("original-task", history.TaskId);
            Assert.True(history.Due.IsRecurring);
            Assert.Equal("Historical instructions", Assert.Single(history.Comments).Content);
            Assert.Equal(InboundEventStatus.Processed, history.Status);
            Assert.Equal("existing-work", Assert.Single(history.Outcomes).WorkItemId);
        }
    }

    private static IDocumentStore? CreateStore(bool isolated = true)
    {
        var connection = Environment.GetEnvironmentVariable("SAMWISE_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connection)) return null;
        return DocumentStore.For(options =>
        {
            options.Connection(connection);
            var serializer = new JsonNetSerializer { EnumStorage = EnumStorage.AsString };
            serializer.Configure(settings =>
            {
                settings.TypeNameHandling = TypeNameHandling.Objects;
                settings.DateTimeZoneHandling = DateTimeZoneHandling.Unspecified;
                settings.DateParseHandling = DateParseHandling.DateTimeOffset;
            });
            options.Serializer(serializer);
            new SamwiseStorageConfiguration().Configure(options);
            if (isolated) options.DatabaseSchemaName = "tasktest_" + Guid.NewGuid().ToString("N");
        });
    }

    private static IRepository<T> Repository<T>(IDocumentStore store) where T : IItemWithId =>
        new NoTenancyMartenRepository<T>(new NoTenancyByDefaultSessionFactory(store, new CurrentTenancyProviderNoTenancy()));

    private static ProcessScheduledTasksUsecaseHandler Handler(IDocumentStore store, IInboundEventProcessingQueue queue,
        DateTimeOffset? now = null) => new(Repository<AgentRoutine>(store), Repository<AgentTask>(store),
        Repository<WorkItem>(store), Repository<InboundEvent>(store), queue, new NoEvents(), new NoNotifications(),
        new Clock(now ?? Now), NullLogger<ProcessScheduledTasksUsecaseHandler>.Instance);

    private sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private sealed class NoNotifications : IPublisher
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
    }
    private sealed class NoEvents : IWorkItemEventPublisher
    { public Task PublishAsync(WorkItem workItem) => Task.CompletedTask; }
    private sealed class RecordingPlanning : IWorkItemPlanningService
    {
        public List<string> Calls { get; } = [];
        public Task PlanAsync(string id, CancellationToken cancellationToken) { Calls.Add(id); return Task.CompletedTask; }
    }
    private sealed class NoIntake : IInboundEventClassificationService
    {
        public Task<IReadOnlyList<string>> ClassifyAsync(string id, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Scheduled explicit instructions must bypass intake.");
    }
}

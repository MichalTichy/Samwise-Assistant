using Samwise.Domain.Inbox;
using Samwise.Inputs.Todoist.Configuration;
using Samwise.Inputs.Todoist.Discovery;
using Samwise.Inputs.Todoist.InboundEvents;
using Samwise.Inputs.Todoist.Ingestion;
using Samwise.Inputs.Todoist.Usecases.Processing;
using Samwise.Logic.Inbox.Services;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Persistence.Repositories;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Tests.Inputs.Todoist;

public sealed class TodoistProcessingTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UnconfiguredTodoistInputRemainsInactive()
    {
        var configuration = new TodoistInputConfiguration();
        var capture = new RecordingCaptureService();
        var handler = CreateHandler(configuration, [], [DueTask("1", "2026-09-21T13:00:00Z")], capture);

        var result = await handler.Handle(new ProcessTodoistInputsUsecase(), CancellationToken.None);

        Assert.Equal(0, result.Imported);
        Assert.Empty(capture.Events);
        Assert.Null(configuration.LastSuccessfulCheckAt);
    }

    [Fact]
    public async Task OnlyTasksInsideTheLeadAndOverdueWindowAreCaptured()
    {
        var configuration = CreateConfiguration(leadHours: 24, overdueDays: 7);
        var capture = new RecordingCaptureService();
        var handler = CreateHandler(
            configuration,
            [],
            [
                DueTask("soon", "2026-09-22T11:00:00Z"),
                DueTask("later", "2026-09-22T13:00:00Z"),
                DueTask("overdue", "2026-09-18"),
                DueTask("forgotten", "2026-09-01"),
                DueTask("tomorrow-all-day", "2026-09-22")
            ],
            capture);

        var result = await handler.Handle(new ProcessTodoistInputsUsecase(), CancellationToken.None);

        Assert.Equal(3, result.Imported);
        Assert.Equal(
            ["overdue", "tomorrow-all-day", "soon"],
            capture.Events.Cast<TodoistTaskInboundEvent>().Select(item => item.TaskId));
        Assert.Equal(Now, configuration.LastSuccessfulCheckAt);
        Assert.Null(configuration.LastError);
    }

    [Fact]
    public async Task CapturedTaskCarriesSourceDetailsCommentsAndGuidance()
    {
        var configuration = CreateConfiguration();
        var capture = new RecordingCaptureService();
        var reader = new StubReader([DueTask("task-1", "2026-09-21T15:00:00Z", labels: ["waiting"])])
        {
            Comments = [new TodoistCommentSnapshot("c-1", Now.AddDays(-1), "Rosie asked for the draft.")]
        };
        var handler = CreateHandler(configuration, [], reader, capture);

        await handler.Handle(new ProcessTodoistInputsUsecase(), CancellationToken.None);

        var inboundEvent = Assert.IsType<TodoistTaskInboundEvent>(Assert.Single(capture.Events));
        Assert.Equal("Todoist", inboundEvent.Source);
        Assert.Equal("task:task-1:due:2026-09-21T15:00:00Z", inboundEvent.ExternalId);
        Assert.Equal("todoist:task:task-1", inboundEvent.ContextKey);
        Assert.Equal(InboundEventStatus.Pending, inboundEvent.Status);
        Assert.Equal("Todoist guidance.", inboundEvent.SourceGuidance);
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 15, 0, 0, TimeSpan.Zero), inboundEvent.DueAt);
        var text = inboundEvent.GetIntakeText();
        Assert.Contains("Todoist task ID: task-1", text);
        Assert.Contains("Project: Inbox", text);
        Assert.Contains("Labels: waiting", text);
        Assert.Contains("Rosie asked for the draft.", text);
    }

    [Fact]
    public async Task IgnoredProjectOrLabelIsRecordedWithoutIntakeOrComments()
    {
        var configuration = CreateConfiguration(
            ignoredProjects: [new TodoistInputSource { Id = "personal", DisplayName = "Personal" }],
            ignoredLabels: [new TodoistInputSource { Id = "manual", DisplayName = "manual" }]);
        var capture = new RecordingCaptureService();
        var reader = new StubReader(
        [
            DueTask("project", "2026-09-21T15:00:00Z", projectId: "personal"),
            DueTask("label", "2026-09-21T15:00:00Z", labels: ["manual"])
        ]);
        var handler = CreateHandler(configuration, [], reader, capture);

        var result = await handler.Handle(new ProcessTodoistInputsUsecase(), CancellationToken.None);

        Assert.Equal(0, result.Imported);
        Assert.Equal(2, result.Ignored);
        Assert.All(capture.Events, item => Assert.Equal(InboundEventStatus.Processed, item.Status));
        Assert.Equal(0, reader.CommentReads);
    }

    [Fact]
    public async Task AlreadyCapturedOccurrenceIsSkippedWithoutReadingComments()
    {
        var configuration = CreateConfiguration();
        var previous = PreviousEvent("task-1", "2026-09-21T15:00:00Z", "work-item-1");
        var capture = new RecordingCaptureService();
        var reader = new StubReader([DueTask("task-1", "2026-09-21T15:00:00Z")]);
        var handler = CreateHandler(configuration, [previous], reader, capture);

        var result = await handler.Handle(new ProcessTodoistInputsUsecase(), CancellationToken.None);

        Assert.Equal(1, result.AlreadyProcessed);
        Assert.Empty(capture.Events);
        Assert.Equal(0, reader.CommentReads);
    }

    [Fact]
    public async Task RescheduledTaskIsANewInputLinkedToTheEarlierOccurrence()
    {
        var configuration = CreateConfiguration();
        var previous = PreviousEvent("task-1", "2026-09-20", "work-item-1");
        var capture = new RecordingCaptureService();
        var handler = CreateHandler(configuration, [previous], [DueTask("task-1", "2026-09-21T18:00:00Z")], capture);

        var result = await handler.Handle(new ProcessTodoistInputsUsecase(), CancellationToken.None);

        Assert.Equal(1, result.Imported);
        var inboundEvent = Assert.IsType<TodoistTaskInboundEvent>(Assert.Single(capture.Events));
        var context = Assert.Single(inboundEvent.Context);
        Assert.True(context.PreviouslyProcessed);
        Assert.Equal(previous.Id, context.InboundEventId);
        Assert.Equal("work-item-1", Assert.Single(context.WorkItemIds));
    }

    [Fact]
    public async Task ReaderFailureIsRecordedOnTheConfiguration()
    {
        var configuration = CreateConfiguration();
        var capture = new RecordingCaptureService();
        var handler = CreateHandler(configuration, [], new StubReader([]) { Failure = "Todoist returned 401: Unauthorized" }, capture);

        var result = await handler.Handle(new ProcessTodoistInputsUsecase(), CancellationToken.None);

        Assert.Equal("Todoist returned 401: Unauthorized", Assert.Single(result.Errors));
        Assert.Equal("Todoist returned 401: Unauthorized", configuration.LastError);
        Assert.Null(configuration.LastSuccessfulCheckAt);
    }

    private static ProcessTodoistInputsUsecaseHandler CreateHandler(
        TodoistInputConfiguration configuration,
        IReadOnlyList<InboundEvent> previousEvents,
        IReadOnlyList<TodoistTaskSnapshot> tasks,
        RecordingCaptureService capture) =>
        CreateHandler(configuration, previousEvents, new StubReader(tasks), capture);

    private static ProcessTodoistInputsUsecaseHandler CreateHandler(
        TodoistInputConfiguration configuration,
        IReadOnlyList<InboundEvent> previousEvents,
        StubReader reader,
        RecordingCaptureService capture) => new(
            new MemoryRepository<TodoistInputConfiguration>([configuration]),
            new MemoryRepository<InboundEvent>(previousEvents),
            reader,
            capture,
            new ImmediateLock(),
            new FixedTimeProvider(Now));

    private static TodoistInputConfiguration CreateConfiguration(
        int leadHours = 24,
        int overdueDays = 7,
        IEnumerable<TodoistInputSource>? ignoredProjects = null,
        IEnumerable<TodoistInputSource>? ignoredLabels = null)
    {
        var configuration = new TodoistInputConfiguration();
        configuration.Update(
            "protected-token",
            15,
            leadHours,
            overdueDays,
            "Todoist guidance.",
            ignoredProjects ?? [],
            ignoredLabels ?? []);
        return configuration;
    }

    private static TodoistTaskSnapshot DueTask(
        string id,
        string due,
        string projectId = "inbox",
        IReadOnlyList<string>? labels = null) => new(
            id,
            $"Task {id}",
            string.Empty,
            projectId,
            projectId == "inbox" ? "Inbox" : projectId,
            labels ?? [],
            1,
            new TodoistDue { Date = due, Timezone = null, Text = "some day" },
            null,
            $"https://app.todoist.com/app/task/{id}");

    private static TodoistTaskInboundEvent PreviousEvent(string taskId, string due, string workItemId) => new()
    {
        Id = $"previous-{taskId}",
        TaskId = taskId,
        Content = $"Task {taskId}",
        Due = new TodoistDue { Date = due },
        ContextKey = TodoistTaskInboundEvent.CreateContextKey(taskId),
        ReceivedAt = Now.AddDays(-1),
        Outcomes =
        [
            new InboundEventOutcome
            {
                Id = "outcome-1",
                WorkItemId = workItemId,
                Kind = InboundEventOutcomeKind.CreatedWorkItem
            }
        ]
    };

    private sealed class StubReader(IReadOnlyList<TodoistTaskSnapshot> tasks) : ITodoistTaskReader
    {
        public IReadOnlyList<TodoistCommentSnapshot> Comments { get; init; } = [];
        public string? Failure { get; init; }
        public int CommentReads { get; private set; }

        public Task<TodoistTaskReadResult> ReadDueTasksAsync(CancellationToken cancellationToken = default) =>
            Failure is null
                ? Task.FromResult(new TodoistTaskReadResult(tasks, "UTC"))
                : throw new InvalidOperationException(Failure);

        public Task<IReadOnlyList<TodoistCommentSnapshot>> ReadCommentsAsync(
            string taskId,
            CancellationToken cancellationToken = default)
        {
            CommentReads++;
            return Task.FromResult(Comments);
        }

        public Task<TodoistInputCatalog> GetCatalogAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingCaptureService : IInboundEventCaptureService
    {
        public List<InboundEvent> Events { get; } = [];

        public Task<InboundEventCaptureResult> CaptureAsync(
            InboundEvent inboundEvent,
            CancellationToken cancellationToken)
        {
            inboundEvent.Id = $"captured-{Events.Count + 1}";
            Events.Add(inboundEvent);
            return Task.FromResult(new InboundEventCaptureResult(inboundEvent.Id, false));
        }

        public Task<InboundEventCaptureResult> CaptureOnlyAsync(
            InboundEvent inboundEvent,
            CancellationToken cancellationToken) => CaptureAsync(inboundEvent, cancellationToken);
    }

    private sealed class ImmediateLock : ITodoistInputProcessingLock
    {
        public Task<IDisposable> EnterAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IDisposable>(new Lease());

        private sealed class Lease : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MemoryRepository<T>(IReadOnlyList<T> initial) : IRepository<T>
        where T : class, IItemWithId
    {
        private readonly List<T> items = [.. initial];

        public Task<T?> GetByIdAsync(string id, CancellationToken cancellationToken = default, string? tenantId = null) =>
            Task.FromResult(items.SingleOrDefault(item => item.Id == id));

        public Task<IReadOnlyList<T>> GetByIdsAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken = default, string? tenantId = null) =>
            Task.FromResult<IReadOnlyList<T>>(items.Where(item => ids.Contains(item.Id)).ToList());

        public Task<T?> GetBySpecAsync(ISpecification<T> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<TResult?> GetBySpecAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<T>> ListAsync(CancellationToken cancellationToken = default, string? tenantId = null) =>
            Task.FromResult<IReadOnlyList<T>>(items);

        public Task<IReadOnlyList<T>> ListAsync(IListSpecification<T> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            Task.FromResult<IReadOnlyList<T>>(items);

        public Task<IReadOnlyList<TResult>> ListAsync<TResult>(IListSpecification<T, TResult> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<int> CountAsync(CancellationToken cancellationToken = default, string? tenantId = null) =>
            Task.FromResult(items.Count);

        public Task AddAsync(ICollection<T> entities, CancellationToken cancellationToken = default, string? tenantId = null)
        {
            items.AddRange(entities);
            return Task.CompletedTask;
        }

        public Task<T> AddAsync(T entity, CancellationToken cancellationToken = default, string? tenantId = null)
        {
            items.Add(entity);
            return Task.FromResult(entity);
        }

        public Task UpdateAsync(T entity, CancellationToken cancellationToken = default, string? tenantId = null) =>
            Task.CompletedTask;

        public Task GetAndUpdateAsync(string id, Func<T, Task> updateMethod, CancellationToken cancellationToken = default, string? tenantId = null) =>
            updateMethod(items.Single(item => item.Id == id));

        public Task DeleteAsync(T entity, CancellationToken cancellationToken = default, string? tenantId = null)
        {
            items.Remove(entity);
            return Task.CompletedTask;
        }

        public Task DeleteByIdAsync(string id, CancellationToken cancellationToken = default, string? tenantId = null)
        {
            items.RemoveAll(item => item.Id == id);
            return Task.CompletedTask;
        }

        public Task DeleteRangeByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default, string? tenantId = null)
        {
            var set = ids.ToHashSet(StringComparer.Ordinal);
            items.RemoveAll(item => set.Contains(item.Id));
            return Task.CompletedTask;
        }
    }
}

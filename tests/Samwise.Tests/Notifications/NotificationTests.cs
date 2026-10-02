using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Samwise.Domain.Notifications;
using Samwise.Domain.WorkItems.Events;
using Samwise.Logic.Notifications;
using Samwise.Logic.Notifications.Services;
using Samwise.Logic.Notifications.Usecases;
using Samwise.Logic.WorkItems.Services;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Persistence.Repositories;
using Shared.Infrastructure.Persistence.Specifications;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;

namespace Samwise.Tests.Notifications;

public sealed class NotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ProposedPlanIsSentToEveryDeviceAsAWorkItemLink()
    {
        var push = new RecordingPushService();
        var handler = CreateHandler(new NotificationSettings(), push);

        await handler.Handle(new PlanProposed("wi-1", "Send Rosie the offer", Now, "plan-1", 1), CancellationToken.None);

        var message = Assert.Single(push.Sent);
        Assert.Equal("Plan ready for approval", message.Title);
        Assert.Equal("Send Rosie the offer", message.Body);
        Assert.Equal("/work-items/wi-1", message.Url);
        Assert.Equal("workitem-wi-1", message.Tag);
        Assert.True(message.Urgent);
    }

    [Fact]
    public async Task DisabledFactsAreNotSent()
    {
        var push = new RecordingPushService();
        var handler = CreateHandler(new NotificationSettings { NotifyNeedsAttention = false, NotifyCompleted = false }, push);

        await handler.Handle(new ExecutionCompleted("wi-1", "Title", Now, "Done"), CancellationToken.None);
        await handler.Handle(new ExecutionOutcomeUnknown("wi-1", "Title", Now, "Lost response"), CancellationToken.None);
        await handler.Handle(new PlanningFailed("wi-1", "Title", Now, null), CancellationToken.None);

        Assert.Empty(push.Sent);
    }

    [Fact]
    public async Task AutomaticallyApprovedCompletionWaitsForTheDailyDigest()
    {
        var push = new RecordingPushService();
        var handler = CreateHandler(new NotificationSettings { NotifyCompleted = true }, push);

        await handler.Handle(new ExecutionCompleted("wi-1", "Title", Now, "Done", AutomaticallyApproved: true), CancellationToken.None);

        Assert.Empty(push.Sent);
    }

    [Fact]
    public async Task DailyDigestLinksToItsReportAndIsSentOnlyOnce()
    {
        var reportDate = new DateOnly(2026, 9, 20);
        var workItem = CompletedAutomatically("Unsubscribe from the newsletter", Now.AddDays(-1));
        var digests = new MemoryRepository<DailyAutoExecutionDigest>([]);
        var push = new RecordingPushService();
        var handler = new SendDailyAutoExecutionSummaryUsecaseHandler(
            new MemoryRepository<NotificationSettings>([new NotificationSettings()]),
            new MemoryRepository<DomainWorkItem>([workItem]),
            digests,
            push,
            new FixedTimeProvider(Now));

        var first = await handler.Handle(new SendDailyAutoExecutionSummaryUsecase(reportDate), CancellationToken.None);
        var second = await handler.Handle(new SendDailyAutoExecutionSummaryUsecase(reportDate), CancellationToken.None);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        var notification = Assert.Single(push.Sent);
        Assert.Equal("1 work item handled automatically", notification.Title);
        Assert.Contains(workItem.Title, notification.Body, StringComparison.Ordinal);
        Assert.Equal("/work-items/auto-executions?date=2026-09-20", notification.Url);
        Assert.Equal(DailyAutoExecutionDigest.IdFor(reportDate), Assert.Single(await digests.ListAsync()).Id);
    }

    [Fact]
    public void DailyReportUsesPragueCalendarDaysAcrossDaylightSavingChanges()
    {
        var spring = AutoExecutionReportPeriod.ForDay(new DateOnly(2026, 3, 29));
        var autumn = AutoExecutionReportPeriod.ForDay(new DateOnly(2026, 10, 25));

        Assert.Equal(TimeSpan.FromHours(23), spring.Until - spring.From);
        Assert.Equal(TimeSpan.FromHours(25), autumn.Until - autumn.From);
    }

    [Fact]
    public void DailyDigestBecomesDueAtEightInPrague()
    {
        Assert.Null(DailyAutoExecutionSchedule.ReportDateDue(
            new DateTimeOffset(2026, 9, 21, 5, 59, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 9, 20), DailyAutoExecutionSchedule.ReportDateDue(
            new DateTimeOffset(2026, 9, 21, 6, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task QuestionPromptIsAppendedAndLongBodiesAreShortened()
    {
        var push = new RecordingPushService();
        var handler = CreateHandler(new NotificationSettings(), push);

        await handler.Handle(
            new PlanningQuestionAsked("wi-1", "Plan the review", Now, "q-1", new string('x', 400)),
            CancellationToken.None);

        var message = Assert.Single(push.Sent);
        Assert.Equal("Samwise has a question", message.Title);
        Assert.StartsWith("Plan the review: xxx", message.Body);
        Assert.Equal(180, message.Body.Length);
        Assert.EndsWith("…", message.Body);
    }

    [Fact]
    public async Task BroadcastRecordsDeliveryRemovesExpiredDevicesAndKeepsFailures()
    {
        var delivered = Device("delivered");
        var expired = Device("expired");
        var failed = Device("failed");
        var throwing = Device("throwing");
        var repository = new MemoryRepository<PushDevice>([delivered, expired, failed, throwing]);
        var sender = new StubSender(device => device.Name switch
        {
            "delivered" => new PushDeliveryResult(PushDeliveryStatus.Delivered),
            "expired" => new PushDeliveryResult(PushDeliveryStatus.Expired),
            "failed" => new PushDeliveryResult(PushDeliveryStatus.Failed, "BadJwtToken"),
            _ => throw new HttpRequestException("offline")
        });
        var service = new PushNotificationService(
            repository,
            sender,
            new FixedTimeProvider(Now),
            NullLogger<PushNotificationService>.Instance);

        var result = await service.SendToAllAsync(Message(), CancellationToken.None);

        Assert.Equal(1, result.Delivered);
        Assert.Equal(1, result.Removed);
        Assert.Equal(["failed: BadJwtToken", "throwing: offline"], result.Errors);
        Assert.Equal(Now, delivered.LastSucceededAt);
        Assert.Equal("BadJwtToken", failed.LastError);
        Assert.DoesNotContain(await repository.ListAsync(), device => device.Name == "expired");
    }

    [Fact]
    public async Task SavingTheSameSubscriptionTwiceUpdatesOneDevice()
    {
        var repository = new MemoryRepository<PushDevice>([]);
        var handler = new SavePushDeviceUsecaseHandler(repository, new FixedTimeProvider(Now));

        var first = await handler.Handle(
            new SavePushDeviceUsecase("https://web.push.apple.com/abc", "key-1", "auth-1", "iPhone · Home Screen app"),
            CancellationToken.None);
        var second = await handler.Handle(
            new SavePushDeviceUsecase("https://web.push.apple.com/abc", "key-2", "auth-2", "My iPhone"),
            CancellationToken.None);

        Assert.Equal(first, second);
        var device = Assert.Single(await repository.ListAsync());
        Assert.Equal("My iPhone", device.Name);
        Assert.Equal("key-2", device.P256dh);
        Assert.Equal(Now, device.CreatedAt);
    }

    [Fact]
    public async Task SubscriptionEndpointMustBeHttps()
    {
        var handler = new SavePushDeviceUsecaseHandler(new MemoryRepository<PushDevice>([]), new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(
            new SavePushDeviceUsecase("http://example.com/push", "key", "auth", "Device"),
            CancellationToken.None));
    }

    [Fact]
    public async Task PublisherPublishesRecordedFactsOnceAndSurvivesFailingHandlers()
    {
        var publisher = new RecordingPublisher(throwFor: typeof(WorkItemCreated));
        var eventPublisher = new WorkItemEventPublisher(publisher, NullLogger<WorkItemEventPublisher>.Instance);
        var workItem = DomainWorkItem.CreateFromInbound("Reply to Rosie.", "Reply to Rosie", Now);
        workItem.Discard(Now.AddMinutes(1));

        await eventPublisher.PublishAsync(workItem);
        await eventPublisher.PublishAsync(workItem);

        Assert.Collection(
            publisher.Published,
            created => Assert.IsType<WorkItemCreated>(created),
            discarded => Assert.Equal("Web", Assert.IsType<WorkItemDiscarded>(discarded).Source));
    }

    private static WorkItemPushNotificationHandler CreateHandler(NotificationSettings settings, RecordingPushService push) =>
        new(new MemoryRepository<NotificationSettings>([settings]), push);

    private static PushDevice Device(string name) => new()
    {
        Id = PushDevice.CreateId($"https://push.example.com/{name}"),
        Name = name,
        Endpoint = $"https://push.example.com/{name}",
        P256dh = "key",
        Auth = "auth",
        CreatedAt = Now.AddDays(-1)
    };

    private static PushNotification Message() => new("Title", "Body", "/board", "tag", Urgent: false);

    private static DomainWorkItem CompletedAutomatically(string title, DateTimeOffset completedAt)
    {
        var workItem = DomainWorkItem.CreateFromInbound(title, title, completedAt.AddMinutes(-5));
        var plan = new Samwise.Domain.WorkItems.WorkItemPlan
        {
            Id = Guid.NewGuid().ToString(),
            WorkItemId = workItem.Id,
            WorkItemRevision = workItem.Revision,
            PlanNumber = workItem.ReservePlanNumber(completedAt.AddMinutes(-4)),
            Summary = title
        };
        workItem.ApplyPlan(plan, completedAt.AddMinutes(-3));
        workItem.Approve(plan.Id, workItem.Revision, completedAt.AddMinutes(-2), DomainWorkItem.AutomaticApprovalSource);
        workItem.StartExecution(plan.Id, workItem.Revision, completedAt.AddMinutes(-1));
        workItem.CompleteExecution(title, [], "invocation-1", completedAt);
        return workItem;
    }

    private sealed class RecordingPushService : IPushNotificationService
    {
        public List<PushNotification> Sent { get; } = [];

        public Task<PushBroadcastResult> SendToAllAsync(PushNotification notification, CancellationToken cancellationToken)
        {
            Sent.Add(notification);
            return Task.FromResult(new PushBroadcastResult(1, 0, []));
        }
    }

    private sealed class StubSender(Func<PushDevice, PushDeliveryResult> send) : IPushNotificationSender
    {
        public Task<PushDeliveryResult> SendAsync(
            PushDevice device,
            PushNotification notification,
            CancellationToken cancellationToken) => Task.FromResult(send(device));
    }

    private sealed class RecordingPublisher(Type throwFor) : IPublisher
    {
        public List<object> Published { get; } = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            Published.Add(notification);
            return notification.GetType() == throwFor
                ? throw new InvalidOperationException("The handler failed.")
                : Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Publish((object)notification!, cancellationToken);
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
            Task.FromResult<IReadOnlyList<T>>(items.ToList());

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

using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Samwise.Domain.Notifications;
using Samwise.Domain.WorkItems;
using Samwise.Logic.Notifications;
using Samwise.Logic.Notifications.Services;
using Samwise.Logic.Notifications.Usecases;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Persistence.Repositories;
using Shared.Infrastructure.Persistence.Specifications;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;

namespace Samwise.Tests.Notifications;

public sealed class NotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EnabledKindIsSentToEveryDeviceAsAWorkItemLink()
    {
        var push = new RecordingPushService();
        var handler = new WorkItemAttentionRequiredHandler(
            new MemoryRepository<NotificationSettings>([new NotificationSettings()]),
            push);

        await handler.Handle(
            new WorkItemAttentionRequired("wi-1", WorkItemStatus.AwaitingApproval, "Send Rosie the offer", null),
            CancellationToken.None);

        var message = Assert.Single(push.Sent);
        Assert.Equal("Plan ready for approval", message.Title);
        Assert.Equal("Send Rosie the offer", message.Body);
        Assert.Equal("/work-items/wi-1", message.Url);
        Assert.Equal("workitem-wi-1", message.Tag);
        Assert.True(message.Urgent);
    }

    [Theory]
    [InlineData(WorkItemStatus.Completed)]
    [InlineData(WorkItemStatus.OutcomeUnknown)]
    [InlineData(WorkItemStatus.Planning)]
    public async Task DisabledOrUninterestingStatusIsNotSent(WorkItemStatus status)
    {
        var push = new RecordingPushService();
        var settings = new NotificationSettings { NotifyNeedsAttention = false, NotifyCompleted = false };
        var handler = new WorkItemAttentionRequiredHandler(new MemoryRepository<NotificationSettings>([settings]), push);

        await handler.Handle(new WorkItemAttentionRequired("wi-1", status, "Title", null), CancellationToken.None);

        Assert.Empty(push.Sent);
    }

    [Fact]
    public void QuestionDetailIsAppendedAndLongBodiesAreShortened()
    {
        var message = WorkItemAttentionRequiredHandler.CreateMessage(new WorkItemAttentionRequired(
            "wi-1",
            WorkItemStatus.AwaitingUserInput,
            "Plan the review",
            new string('x', 400)));

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
    public async Task NotifierNeverThrowsIntoTheWorkflow()
    {
        var notifier = new WorkItemAttentionNotifier(
            new ThrowingPublisher(),
            NullLogger<WorkItemAttentionNotifier>.Instance);
        var workItem = DomainWorkItem.CreateFromInbound("Reply to Rosie.", "Reply to Rosie", Now);

        await notifier.NotifyAsync(workItem);
    }

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

    private sealed class ThrowingPublisher : IPublisher
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The push service is down.");

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            throw new InvalidOperationException("The push service is down.");
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

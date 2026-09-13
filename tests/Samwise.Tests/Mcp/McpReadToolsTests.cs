using System.Text.Json;
using Samwise.Domain.Inbox;
using Samwise.Domain.WorkItems;
using Samwise.Infrastructure.Mcp.Tools;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Persistence.Repositories;
using Shared.Infrastructure.Persistence.Specifications;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;

namespace Samwise.Tests.Mcp;

public sealed class McpReadToolsTests
{
    [Fact]
    public async Task WorkItemListReturnsCompactOperationalSummary()
    {
        var workItem = DomainWorkItem.CreateFromInbound(
            new string('x', 600),
            "Long Work Item",
            DateTimeOffset.Parse("2026-09-13T08:00:00Z"));
        var tools = new WorkItemTools(new ReadOnlyRepository<DomainWorkItem>([workItem]));

        var json = await tools.ListWorkItemsAsync(cancellationToken: CancellationToken.None);
        using var document = JsonDocument.Parse(json);
        var summary = document.RootElement.GetProperty("workItems")[0];

        Assert.Equal(workItem.Id, summary.GetProperty("id").GetString());
        Assert.Equal("Planning", summary.GetProperty("status").GetString());
        Assert.True(summary.GetProperty("descriptionPreview").GetString()!.Length <= 500);
        Assert.False(summary.TryGetProperty("plans", out _));
    }

    [Fact]
    public async Task InputEventDetailKeepsSourceSpecificEmailContent()
    {
        var inputEvent = new EmailInboundEvent
        {
            Id = "event-1",
            EmailAccountId = "account-1",
            Subject = "Specific subject",
            FromAddress = "sender@example.com",
            BodyText = "Complete message body",
            ReceivedAt = DateTimeOffset.Parse("2026-09-13T08:00:00Z")
        };
        var tools = new InputEventTools(new ReadOnlyRepository<InboundEvent>([inputEvent]));

        var json = await tools.GetInputEventAsync(inputEvent.Id, CancellationToken.None);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("Email", document.RootElement.GetProperty("source").GetString());
        Assert.Equal("Specific subject", document.RootElement.GetProperty("subject").GetString());
        Assert.Equal("Complete message body", document.RootElement.GetProperty("bodyText").GetString());
    }

    private sealed class ReadOnlyRepository<T>(IReadOnlyList<T> items) : IReadOnlyRepository<T>
        where T : IItemWithId
    {
        public Task<T?> GetByIdAsync(
            string id,
            CancellationToken cancellationToken = default,
            string? tenantId = null) =>
            Task.FromResult(items.SingleOrDefault(item => item.Id == id));

        public Task<IReadOnlyList<T>> ListAsync(
            IListSpecification<T> specification,
            CancellationToken cancellationToken = default,
            string? tenantId = null) =>
            Task.FromResult(items);

        public Task<IReadOnlyList<T>> GetByIdsAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<T?> GetBySpecAsync(ISpecification<T> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<TResult?> GetBySpecAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<T>> ListAsync(CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TResult>> ListAsync<TResult>(IListSpecification<T, TResult> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<int> CountAsync(CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();
    }
}

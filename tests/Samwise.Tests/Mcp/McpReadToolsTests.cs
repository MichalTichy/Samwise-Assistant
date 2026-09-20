using System.Text.Json;
using Samwise.Domain.Agents;
using Samwise.Domain.Inbox;
using Samwise.Domain.WorkItems;
using Samwise.Infrastructure.Mcp.Tools;
using Samwise.Logic.Inputs.Email.Events;
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
    public async Task AgentInvocationDetailReturnsTheCompleteRecordedExchangeAndToolSnapshot()
    {
        var invocation = new AgentInvocation
        {
            Id = "invocation-1",
            WorkItemId = "work-item-1",
            WorkItemRevision = 3,
            Profile = "Planning",
            Provider = "OpenAI",
            Model = "gpt-5.6-sol",
            ReasoningEffort = "high",
            RenderedPrompt = "The exact rendered prompt.",
            RawResponse = "{\"result\":\"the complete response\"}",
            Error = "A recorded diagnostic.",
            Status = AgentInvocationStatus.Succeeded,
            StartedAt = DateTimeOffset.Parse("2026-09-13T08:00:00Z"),
            FinishedAt = DateTimeOffset.Parse("2026-09-13T08:00:10Z"),
            AvailableTools = new AgentInvocationToolCatalog
            {
                CatalogVersion = "catalog-1",
                RawJson = "{\"tools\":[{\"id\":\"mail/reply_email\"}]}",
                Tools =
                [
                    new AgentInvocationTool
                    {
                        Id = "mail/reply_email",
                        Name = "reply_email",
                        Description = "Reply in the existing email thread.",
                        InputSchemaJson = "{\"type\":\"object\"}",
                        CanInvoke = true
                    }
                ]
            }
        };
        var tools = new AgentInvocationTools(new ReadOnlyRepository<AgentInvocation>([invocation]));

        var json = await tools.GetAgentInvocationAsync(invocation.Id, CancellationToken.None);
        using var document = JsonDocument.Parse(json);
        var result = document.RootElement;

        Assert.Equal(invocation.RenderedPrompt, result.GetProperty("renderedPrompt").GetString());
        Assert.Equal(invocation.RawResponse, result.GetProperty("rawResponse").GetString());
        Assert.Equal(invocation.Error, result.GetProperty("error").GetString());
        Assert.Equal("mail/reply_email", result.GetProperty("availableTools").GetProperty("tools")[0].GetProperty("id").GetString());
        Assert.Equal(invocation.AvailableTools.RawJson, result.GetProperty("availableTools").GetProperty("rawJson").GetString());
    }

    [Fact]
    public async Task AgentInvocationListReturnsCompactWorkItemDiagnostics()
    {
        var invocation = new AgentInvocation
        {
            Id = "invocation-1",
            WorkItemId = "work-item-1",
            WorkItemRevision = 3,
            Profile = "Planning",
            Provider = "OpenAI",
            Model = "gpt-5.6-sol",
            ReasoningEffort = "high",
            RenderedPrompt = "The exact rendered prompt.",
            RawResponse = "The complete raw response, which must not be returned by the list tool.",
            Error = new string('x', 600),
            Status = AgentInvocationStatus.Failed,
            StartedAt = DateTimeOffset.Parse("2026-09-13T08:00:00Z"),
            FinishedAt = DateTimeOffset.Parse("2026-09-13T08:00:10Z")
        };
        var tools = new AgentInvocationTools(new ReadOnlyRepository<AgentInvocation>([invocation]));

        var json = await tools.ListAgentInvocationsAsync(
            invocation.WorkItemId,
            cancellationToken: CancellationToken.None);
        using var document = JsonDocument.Parse(json);
        var result = document.RootElement;
        var summary = result.GetProperty("invocations")[0];

        Assert.Equal(1, result.GetProperty("count").GetInt32());
        Assert.Equal(invocation.Id, summary.GetProperty("id").GetString());
        Assert.Equal("Failed", summary.GetProperty("status").GetString());
        Assert.True(summary.GetProperty("errorPreview").GetString()!.Length <= 500);
        Assert.False(summary.TryGetProperty("renderedPrompt", out _));
        Assert.False(summary.TryGetProperty("rawResponse", out _));
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

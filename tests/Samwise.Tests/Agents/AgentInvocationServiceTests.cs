using Samwise.Domain.Agents;
using Samwise.Logic.Agents;
using Samwise.Logic.Agents.Services;
using Shared.Infrastructure.Persistence.Repositories;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Tests.Agents;

public sealed class AgentInvocationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(AgentStage.Intake, "Intake")]
    [InlineData(AgentStage.Planning, "Planning")]
    [InlineData(AgentStage.Execution, "Execution")]
    [InlineData(AgentStage.AutomaticApproval, "Automatic approval")]
    [InlineData(AgentStage.AutoApprovedExecution, "Auto-approved execution")]
    public async Task ServicePersistsReadableProfileForEachStage(AgentStage stage, string expectedProfile)
    {
        var service = new AgentInvocationService(new AgentInvocationRepository(), new FixedTimeProvider(Now));

        var invocation = await service.StartAsync(
            new AgentInvocationStart(stage, "Prompt", null, null, 0, new AgentRunConfiguration("model", "high")),
            CancellationToken.None);

        Assert.Equal(expectedProfile, invocation.Profile);
    }

    [Fact]
    public async Task ServicePersistsInvocationLifecycle()
    {
        var repository = new AgentInvocationRepository();
        var timeProvider = new FixedTimeProvider(Now);
        var service = new AgentInvocationService(repository, timeProvider);

        var invocation = await service.StartAsync(
            new AgentInvocationStart(
                AgentStage.Planning,
                "Rendered prompt",
                null,
                "work-item-1",
                3,
                new AgentRunConfiguration("planning-model", "max"),
                new ToolCatalogSnapshot(
                    "catalog-version",
                    "{\"catalog_version\":\"catalog-version\"}",
                    [new ToolServerCapability(
                        "mail",
                        "Mail",
                        "1.0.0",
                        false,
                        null,
                        null,
                        null,
                        [new ToolCapability(
                            "mail/search",
                            "Search mail",
                            "Search messages.",
                            "{\"type\":\"object\"}",
                            true)])])),
            CancellationToken.None);

        Assert.Equal(AgentInvocationStatus.Running, invocation.Status);
        Assert.Equal("Planning", invocation.Profile);
        Assert.Equal("work-item-1", invocation.WorkItemId);
        Assert.Equal(3, invocation.WorkItemRevision);
        Assert.Equal("planning-model", invocation.Model);
        Assert.Equal("max", invocation.ReasoningEffort);
        var availableTools = Assert.IsType<AgentInvocationToolCatalog>(invocation.AvailableTools);
        Assert.Equal("catalog-version", availableTools.CatalogVersion);
        Assert.Equal("{\"catalog_version\":\"catalog-version\"}", availableTools.RawJson);
        var tool = Assert.Single(availableTools.Tools);
        Assert.Equal("mail/search", tool.Id);
        Assert.Equal("Search mail", tool.Name);
        Assert.True(tool.CanInvoke);
        Assert.Equal(Now, invocation.StartedAt);

        var receipt = new AgentReceipt("Codex CLI Gateway", "gpt-5", "completion-1", "raw response");
        timeProvider.SetUtcNow(Now.AddSeconds(5));
        await service.CompleteAsync(invocation.Id, receipt, CancellationToken.None);

        var completed = await repository.GetByIdAsync(invocation.Id);
        Assert.NotNull(completed);
        Assert.Equal(AgentInvocationStatus.Succeeded, completed.Status);
        Assert.Equal(receipt.Provider, completed.Provider);
        Assert.Equal(receipt.Model, completed.Model);
        Assert.Equal(receipt.CompletionId, completed.CompletionId);
        Assert.Equal(receipt.RawResponse, completed.RawResponse);
        Assert.Equal(Now.AddSeconds(5), completed.FinishedAt);
    }

    [Theory]
    [InlineData(AgentInvocationStatus.Failed)]
    [InlineData(AgentInvocationStatus.OutcomeUnknown)]
    public async Task ServicePersistsAllowedFailureStatus(AgentInvocationStatus status)
    {
        var repository = new AgentInvocationRepository();
        var timeProvider = new FixedTimeProvider(Now);
        var service = new AgentInvocationService(repository, timeProvider);
        var invocation = await service.StartAsync(
            new AgentInvocationStart(
                AgentStage.Execution,
                "Rendered prompt",
                null,
                "work-item-1",
                3,
                new AgentRunConfiguration("execution-model", "high")),
            CancellationToken.None);
        timeProvider.SetUtcNow(Now.AddSeconds(5));

        await service.FailAsync(
            invocation.Id,
            status,
            "Gateway unavailable",
            CancellationToken.None);

        var failed = await repository.GetByIdAsync(invocation.Id);
        Assert.NotNull(failed);
        Assert.Equal(status, failed.Status);
        Assert.Equal("Gateway unavailable", failed.Error);
        Assert.Equal(Now.AddSeconds(5), failed.FinishedAt);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset currentUtcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => currentUtcNow;

        public void SetUtcNow(DateTimeOffset value) => currentUtcNow = value;
    }

    private sealed class AgentInvocationRepository : IRepository<AgentInvocation>
    {
        private readonly Dictionary<string, AgentInvocation> invocations = [];

        public Task<AgentInvocation> AddAsync(
            AgentInvocation entity,
            CancellationToken cancellationToken = default,
            string? tenantId = null)
        {
            invocations.Add(entity.Id, entity);
            return Task.FromResult(entity);
        }

        public Task<AgentInvocation?> GetByIdAsync(
            string id,
            CancellationToken cancellationToken = default,
            string? tenantId = null) =>
            Task.FromResult(invocations.GetValueOrDefault(id));

        public Task UpdateAsync(
            AgentInvocation entity,
            CancellationToken cancellationToken = default,
            string? tenantId = null)
        {
            invocations[entity.Id] = entity;
            return Task.CompletedTask;
        }

        public Task AddAsync(ICollection<AgentInvocation> entities, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AgentInvocation>> GetByIdsAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<AgentInvocation?> GetBySpecAsync(ISpecification<AgentInvocation> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<TResult?> GetBySpecAsync<TResult>(ISpecification<AgentInvocation, TResult> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AgentInvocation>> ListAsync(CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AgentInvocation>> ListAsync(IListSpecification<AgentInvocation> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TResult>> ListAsync<TResult>(IListSpecification<AgentInvocation, TResult> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<int> CountAsync(CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task GetAndUpdateAsync(string id, Func<AgentInvocation, Task> updateMethod, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task DeleteAsync(AgentInvocation entity, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task DeleteByIdAsync(string id, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task DeleteRangeByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();
    }
}

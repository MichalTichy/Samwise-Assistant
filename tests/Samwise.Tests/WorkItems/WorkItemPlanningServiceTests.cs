using Microsoft.Extensions.Logging.Abstractions;
using Samwise.Domain.Agents;
using Samwise.Domain.Playbooks;
using Samwise.Domain.WorkItems;
using Samwise.Logic.Agents;
using Samwise.Logic.Agents.Services;
using Samwise.Logic.Prompts.Services;
using Samwise.Logic.WorkItems.Services;
using Shared.Infrastructure.Persistence.Repositories;
using Shared.Infrastructure.Persistence.Specifications;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;

namespace Samwise.Tests.WorkItems;

public sealed class WorkItemPlanningServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AlreadyResolvedCompletesWithoutCreatingAPlan()
    {
        var workItem = DomainWorkItem.CreateFromInbound("Reply to Rosie.", "Reply to Rosie", Now);
        var invocationService = new StubInvocationService();
        var service = CreateService(
            workItem,
            new PlannerResult(
                PlanningDisposition.AlreadyResolved,
                "The newly captured sent email contains the requested reply.",
                EmptyPlan(),
                Receipt()),
            invocationService);

        await service.PlanAsync(workItem.Id, CancellationToken.None);

        Assert.Equal(WorkItemStatus.Completed, workItem.Status);
        Assert.Empty(workItem.Plans);
        var completion = Assert.IsType<WorkItemCompletion>(workItem.Completion);
        Assert.Equal(WorkItemCompletionKind.PlanningResolution, completion.Kind);
        Assert.Equal(invocationService.Invocation.Id, completion.AgentInvocationId);
        Assert.True(invocationService.Completed);
    }

    [Fact]
    public async Task NoLongerRelevantDiscardsWithoutCreatingAPlan()
    {
        var workItem = DomainWorkItem.CreateFromInbound("Arrange an optional meeting.", null, Now);
        var invocationService = new StubInvocationService();
        var service = CreateService(
            workItem,
            new PlannerResult(
                PlanningDisposition.NoLongerRelevant,
                "The latest context says the meeting is no longer needed.",
                EmptyPlan(),
                Receipt()),
            invocationService);

        await service.PlanAsync(workItem.Id, CancellationToken.None);

        Assert.Equal(WorkItemStatus.Discarded, workItem.Status);
        Assert.Empty(workItem.Plans);
        Assert.Equal("Planning agent", workItem.DiscardedFrom);
        Assert.Equal("The latest context says the meeting is no longer needed.", workItem.DiscardReason);
        Assert.Equal(invocationService.Invocation.Id, workItem.DiscardInvocationId);
        Assert.True(invocationService.Completed);
    }

    private static WorkItemPlanningService CreateService(
        DomainWorkItem workItem,
        PlannerResult result,
        StubInvocationService invocationService) => new(
            new WorkItemRepository(workItem),
            new StubAgentRuntime(result),
            new StubPromptBuilder(),
            invocationService,
            new FixedTimeProvider(Now.AddMinutes(1)),
            NullLogger<WorkItemPlanningService>.Instance);

    private static PlanDraft EmptyPlan() => new(null, string.Empty, [], [], []);

    private static AgentReceipt Receipt() => new("Test", "test-model", "completion-1", "{}");

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class StubAgentRuntime(PlannerResult result) : IAgentRuntime
    {
        public Task<IntakeResult> ClassifyAsync(
            IntakeRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PlannerResult> PlanAsync(
            PlannerRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(result);

        public Task<ExecutionResult> ExecuteAsync(
            ExecutionRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubPromptBuilder : IAgentPromptBuilder
    {
        private static readonly ToolCatalogSnapshot EmptyCatalog = new("empty", "{}", []);

        public Task<IntakeAgentPrompt> BuildIntakeAsync(
            string inboundEventId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PlanningAgentPrompt> BuildPlanningAsync(
            string workItemId,
            CancellationToken cancellationToken) => Task.FromResult(new PlanningAgentPrompt(
                "Plan the work.",
                new AgentRunConfiguration("test-model", "medium"),
                EmptyCatalog,
                EmptyCatalog,
                Array.Empty<Playbook>(),
                AgentConfiguration.DocumentId,
                Now));

        public Task<ExecutionAgentPrompt> BuildExecutionAsync(
            string workItemId,
            int workItemRevision,
            string planId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubInvocationService : IAgentInvocationService
    {
        public AgentInvocation Invocation { get; } = new()
        {
            Id = "planning-invocation-1",
            Profile = "Planning",
            Status = AgentInvocationStatus.Running
        };

        public bool Completed { get; private set; }

        public Task<AgentInvocation> StartAsync(
            AgentInvocationStart request,
            CancellationToken cancellationToken) => Task.FromResult(Invocation);

        public Task CompleteAsync(
            string agentInvocationId,
            AgentReceipt receipt,
            CancellationToken cancellationToken)
        {
            Assert.Equal(Invocation.Id, agentInvocationId);
            Completed = true;
            return Task.CompletedTask;
        }

        public Task FailAsync(
            string agentInvocationId,
            AgentInvocationStatus status,
            string error,
            CancellationToken cancellationToken) => throw new InvalidOperationException(error);
    }

    private sealed class WorkItemRepository(DomainWorkItem workItem) : IRepository<DomainWorkItem>
    {
        public Task<DomainWorkItem?> GetByIdAsync(
            string id,
            CancellationToken cancellationToken = default,
            string? tenantId = null) => Task.FromResult(id == workItem.Id ? workItem : null);

        public Task UpdateAsync(
            DomainWorkItem entity,
            CancellationToken cancellationToken = default,
            string? tenantId = null) => Task.CompletedTask;

        public Task<DomainWorkItem> AddAsync(
            DomainWorkItem entity,
            CancellationToken cancellationToken = default,
            string? tenantId = null) => throw new NotSupportedException();

        public Task AddAsync(
            ICollection<DomainWorkItem> entities,
            CancellationToken cancellationToken = default,
            string? tenantId = null) => throw new NotSupportedException();

        public Task<IReadOnlyList<DomainWorkItem>> GetByIdsAsync(
            IReadOnlyList<string> ids,
            CancellationToken cancellationToken = default,
            string? tenantId = null) => throw new NotSupportedException();

        public Task<DomainWorkItem?> GetBySpecAsync(
            ISpecification<DomainWorkItem> specification,
            CancellationToken cancellationToken = default,
            string? tenantId = null) => throw new NotSupportedException();

        public Task<TResult?> GetBySpecAsync<TResult>(
            ISpecification<DomainWorkItem, TResult> specification,
            CancellationToken cancellationToken = default,
            string? tenantId = null) => throw new NotSupportedException();

        public Task<IReadOnlyList<DomainWorkItem>> ListAsync(
            CancellationToken cancellationToken = default,
            string? tenantId = null) => throw new NotSupportedException();

        public Task<IReadOnlyList<DomainWorkItem>> ListAsync(
            IListSpecification<DomainWorkItem> specification,
            CancellationToken cancellationToken = default,
            string? tenantId = null) => throw new NotSupportedException();

        public Task<IReadOnlyList<TResult>> ListAsync<TResult>(
            IListSpecification<DomainWorkItem, TResult> specification,
            CancellationToken cancellationToken = default,
            string? tenantId = null) => throw new NotSupportedException();

        public Task<int> CountAsync(
            CancellationToken cancellationToken = default,
            string? tenantId = null) => throw new NotSupportedException();

        public Task GetAndUpdateAsync(
            string id,
            Func<DomainWorkItem, Task> updateMethod,
            CancellationToken cancellationToken = default,
            string? tenantId = null) => throw new NotSupportedException();

        public Task DeleteAsync(
            DomainWorkItem entity,
            CancellationToken cancellationToken = default,
            string? tenantId = null) => throw new NotSupportedException();

        public Task DeleteByIdAsync(
            string id,
            CancellationToken cancellationToken = default,
            string? tenantId = null) => throw new NotSupportedException();

        public Task DeleteRangeByIdsAsync(
            IEnumerable<string> ids,
            CancellationToken cancellationToken = default,
            string? tenantId = null) => throw new NotSupportedException();
    }
}

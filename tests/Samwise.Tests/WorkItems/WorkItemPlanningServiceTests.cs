using Microsoft.Extensions.Logging.Abstractions;
using Samwise.Domain.Agents;
using Samwise.Domain.Playbooks;
using Samwise.Domain.Tasks;
using Shared.Infrastructure.Persistence;
using Samwise.Domain.WorkItems;
using Samwise.Domain.WorkItems.Events;
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

    [Theory]
    [InlineData(ExecutionDisposition.Succeeded, WorkItemStatus.Completed)]
    [InlineData(ExecutionDisposition.Failed, WorkItemStatus.NeedsAttention)]
    [InlineData(ExecutionDisposition.OutcomeUnknown, WorkItemStatus.OutcomeUnknown)]
    public async Task ExecutionPreservesWaitingFailureAndUncertaintyAsDistinctOutcomes(
        ExecutionDisposition disposition, WorkItemStatus expectedStatus)
    {
        var workItem = DomainWorkItem.CreateFromInbound("Deliver invoice 2884.", null, Now);
        var plan = new WorkItemPlan
        {
            Id = "plan-1", WorkItemId = workItem.Id, WorkItemRevision = workItem.Revision,
            PlanNumber = 1, Summary = "Create invoice 2884.",
            ExecutionTools = new PlanToolCatalogSnapshot { CatalogVersion = "empty" }, CreatedAt = Now
        };
        workItem.ApplyPlan(plan, Now);
        workItem.Approve(plan.Id, workItem.Revision, Now);
        var runtime = new StubAgentRuntime(null, new ExecutionResult(
            disposition, "Invoice 2884: verified current result.", ["Exact invoice ID: 2884"], Receipt()));
        var invocations = new StubInvocationService();
        var events = new RecordingEventPublisher();
        var service = new WorkItemExecutionService(new WorkItemRepository(workItem), runtime,
            new StubPromptBuilder(), invocations, events, new FixedTimeProvider(Now),
            NullLogger<WorkItemExecutionService>.Instance);
        var request = new WorkItemExecutionRequest(workItem.Id, plan.Id, workItem.Revision);

        await service.ExecuteAsync(request, CancellationToken.None);
        await service.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(expectedStatus, workItem.Status);
        Assert.Equal(1, runtime.ExecutionCalls);
        Assert.True(invocations.Completed);
        if (disposition != ExecutionDisposition.Succeeded)
        {
            Assert.Null(workItem.Completion);
            Assert.Null(workItem.CompletedAt);
            Assert.DoesNotContain(events.Published, e => e is ExecutionCompleted);
            Assert.Contains(workItem.Comments, c => c.Body.Contains("Exact invoice ID: 2884", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task AlreadyResolvedCompletesWithoutCreatingAPlan()
    {
        var workItem = DomainWorkItem.CreateFromInbound("Reply to Rosie.", "Reply to Rosie", Now);
        var invocationService = new StubInvocationService();
        var events = new RecordingEventPublisher();
        var service = CreateService(
            workItem,
            new PlannerResult(
                PlanningDisposition.AlreadyResolved,
                "The newly captured sent email contains the requested reply.",
                EmptyPlan(),
                null,
                Receipt()),
            invocationService,
            events);

        await service.PlanAsync(workItem.Id, CancellationToken.None);

        Assert.Equal(WorkItemStatus.Completed, workItem.Status);
        Assert.Empty(workItem.Plans);
        var completion = Assert.IsType<WorkItemCompletion>(workItem.Completion);
        Assert.Equal(WorkItemCompletionKind.PlanningResolution, completion.Kind);
        Assert.Equal(invocationService.Invocation.Id, completion.AgentInvocationId);
        Assert.True(invocationService.Completed);
        var resolved = Assert.IsType<WorkItemResolvedDuringPlanning>(events.Published[^1]);
        Assert.Equal("The newly captured sent email contains the requested reply.", resolved.Reason);
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
                null,
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

    [Fact]
    public async Task UserInputRequiredStoresQuestionWithoutCreatingAPlan()
    {
        var workItem = DomainWorkItem.CreateFromInbound("Schedule the proposed meeting.", null, Now);
        var invocationService = new StubInvocationService();
        var events = new RecordingEventPublisher();
        var service = CreateService(
            workItem,
            new PlannerResult(
                PlanningDisposition.UserInputRequired,
                null,
                EmptyPlan(),
                new PlanningQuestionDraft(
                    "How should the conflict be handled?",
                    "Both offered times overlap another meeting.",
                    [
                        new PlanningQuestionOptionDraft("Ask for alternatives", "Do not select either time."),
                        new PlanningQuestionOptionDraft("Use Tuesday", "Reschedule the conflicting meeting.")
                    ]),
                Receipt()),
            invocationService,
            events);

        await service.PlanAsync(workItem.Id, CancellationToken.None);

        Assert.Equal(WorkItemStatus.AwaitingUserInput, workItem.Status);
        Assert.Empty(workItem.Plans);
        var question = Assert.IsType<PlanningQuestion>(workItem.PendingPlanningQuestion);
        Assert.Equal("How should the conflict be handled?", question.Prompt);
        Assert.Equal(2, question.Options.Count);
        Assert.Equal(invocationService.Invocation.Id, question.PlanningInvocationId);
        Assert.True(invocationService.Completed);
        var asked = Assert.IsType<PlanningQuestionAsked>(events.Published[^1]);
        Assert.Equal(question.Id, asked.QuestionId);
        Assert.Equal("How should the conflict be handled?", asked.Prompt);
    }

    [Theory]
    [InlineData("Todoist", PlanningDisposition.NoLongerRelevant, WorkItemStatus.Discarded)]
    [InlineData("Todoist", PlanningDisposition.AlreadyResolved, WorkItemStatus.NeedsAttention)]
    [InlineData(null, PlanningDisposition.NoLongerRelevant, WorkItemStatus.NeedsAttention)]
    [InlineData(null, PlanningDisposition.AlreadyResolved, WorkItemStatus.NeedsAttention)]
    public async Task HumanTodoistAssessmentCanEndWithoutCompletingTheTaskButInternalWorkNeedsProgress(
        string? provider, PlanningDisposition disposition, WorkItemStatus expected)
    {
        var task = new AgentTask { Title = "Collect parcel", Instructions = "Collect parcel 123",
            Checklist = [new() { Text = "Parcel collected" }], OccurrenceAt = Now,
            ExternalSource = provider is null ? null : new() { Provider = provider, TaskId = "123", DueAt = Now } };
        var workItem = DomainWorkItem.CreateFromInbound(task.Instructions, task.Title, Now);
        workItem.AgentTaskId = task.Id;
        task.WorkItemIds.Add(workItem.Id);
        var service = CreateService(workItem, new PlannerResult(disposition,
            "Human collection remains open; there is no useful current action or justified follow-up check.",
            EmptyPlan(), null, Receipt()), new StubInvocationService(), tasks: new ReadOnlyRepository<AgentTask>([task]));

        await service.PlanAsync(workItem.Id, CancellationToken.None);

        Assert.Equal(expected, workItem.Status);
        Assert.Null(task.CompletedAt);
        Assert.Null(task.NextRunAt);
        Assert.False(Assert.Single(task.Checklist).IsCompleted);
        Assert.Empty(workItem.Plans);
        if (provider is not null) Assert.True(task.ExternalSource!.IsActive);
    }

    private static WorkItemPlanningService CreateService(
        DomainWorkItem workItem,
        PlannerResult result,
        StubInvocationService invocationService,
        RecordingEventPublisher? events = null,
        IReadOnlyRepository<AgentTask>? tasks = null) => new(
            new WorkItemRepository(workItem),
            new StubAgentRuntime(result),
            new StubPromptBuilder(),
            invocationService,
            events ?? new RecordingEventPublisher(),
            new StubAutomaticApprovalService(),
            new FixedTimeProvider(Now.AddMinutes(1)),
            NullLogger<WorkItemPlanningService>.Instance, tasks);

    private static PlanDraft EmptyPlan() => new(null, string.Empty, [], [], []);

    private static AgentReceipt Receipt() => new("Test", "test-model", "completion-1", "{}");

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class StubAgentRuntime(PlannerResult? result, ExecutionResult? executionResult = null) : IAgentRuntime
    {
        public int ExecutionCalls { get; private set; }
        public Task<IntakeResult> ClassifyAsync(
            IntakeRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PlannerResult> PlanAsync(
            PlannerRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(result!);

        public Task<ExecutionResult> ExecuteAsync(
            ExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            ExecutionCalls++;
            return Task.FromResult(executionResult ?? throw new NotSupportedException());
        }

        public Task<AutomaticApprovalResult> AssessAutomaticApprovalAsync(
            AutomaticApprovalRequest request,
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
            CancellationToken cancellationToken,
            AgentStage stage = AgentStage.Execution) => Task.FromResult(new ExecutionAgentPrompt(
                "Execute the exact plan.", new AgentRunConfiguration("test-model", "medium"), EmptyCatalog));

        public Task<AutomaticApprovalAgentPrompt> BuildAutomaticApprovalAsync(
            string workItemId,
            int workItemRevision,
            string planId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubAutomaticApprovalService : IWorkItemAutomaticApprovalService
    {
        public Task AssessAsync(string workItemId, string planId, int workItemRevision,
            CancellationToken cancellationToken) => Task.CompletedTask;
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

    private sealed class RecordingEventPublisher : IWorkItemEventPublisher
    {
        public List<WorkItemEvent> Published { get; } = [];

        public Task PublishAsync(DomainWorkItem workItem)
        {
            Published.AddRange(workItem.DequeueEvents());
            return Task.CompletedTask;
        }
    }
    private sealed class ReadOnlyRepository<T>(IReadOnlyList<T> items) : IReadOnlyRepository<T>
        where T : IItemWithId
    {
        public Task<T?> GetByIdAsync(
            string id,
            CancellationToken cancellationToken = default,
            string? tenantId = null) =>
            Task.FromResult(items.SingleOrDefault(item => item.Id == id));

        public Task<IReadOnlyList<T>> GetByIdsAsync(
            IReadOnlyList<string> ids,
            CancellationToken cancellationToken = default,
            string? tenantId = null) =>
            Task.FromResult<IReadOnlyList<T>>(items.Where(item => ids.Contains(item.Id)).ToList());

        public Task<T?> GetBySpecAsync(
            ISpecification<T> specification,
            CancellationToken cancellationToken = default,
            string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<TResult?> GetBySpecAsync<TResult>(
            ISpecification<T, TResult> specification,
            CancellationToken cancellationToken = default,
            string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<T>> ListAsync(
            CancellationToken cancellationToken = default,
            string? tenantId = null) =>
            Task.FromResult(items);

        public Task<IReadOnlyList<T>> ListAsync(
            IListSpecification<T> specification,
            CancellationToken cancellationToken = default,
            string? tenantId = null) =>
            Task.FromResult(items);

        public Task<IReadOnlyList<TResult>> ListAsync<TResult>(
            IListSpecification<T, TResult> specification,
            CancellationToken cancellationToken = default,
            string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<int> CountAsync(
            CancellationToken cancellationToken = default,
            string? tenantId = null) =>
            Task.FromResult(items.Count);
    }
}

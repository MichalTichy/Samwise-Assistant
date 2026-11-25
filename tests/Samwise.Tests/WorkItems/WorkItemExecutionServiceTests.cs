using Microsoft.Extensions.Logging.Abstractions;
using Samwise.Domain.Agents;
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

public sealed class WorkItemExecutionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    private const string ToolId = "todoist/create_task";
    private const string ReadToolId = "todoist/find_tasks";
    private const string Schema = """{"type":"object","properties":{"content":{"type":"string"}},"required":["content"]}""";
    private static readonly ToolCapability PlannedTool = new(ToolId, "create_task", "Create a task.", Schema, true);
    private static readonly ToolCapability ReadTool = new(ReadToolId, "find_tasks", "Find existing tasks.", "{\"type\":\"object\"}", true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnrelatedCatalogChangesPreserveApprovalAndExecuteTheSamePlan(bool automatic)
    {
        var workItem = CreateApprovedWorkItem(automatic);
        var plan = workItem.FindCurrentPlan()!;
        var approval = plan.Review;
        var current = Catalog("new-catalog-version", ReadTool, PlannedTool,
            new ToolCapability("wallet/get_records", "get_records", "Changed unrelated tool.", "false", true),
            new ToolCapability("wallet/new_tool", "new_tool", null, "{}", true));
        var runtime = new RecordingRuntime();
        var invocations = new RecordingInvocationService();
        var events = new RecordingEventPublisher();
        var promptBuilder = new StubPromptBuilder(current);

        await CreateService(workItem, runtime, invocations, events, promptBuilder)
            .ExecuteAsync(new WorkItemExecutionRequest(workItem.Id, plan.Id, workItem.Revision), CancellationToken.None);

        Assert.Equal(WorkItemStatus.Completed, workItem.Status);
        Assert.Same(plan, Assert.Single(workItem.Plans));
        Assert.Same(approval, plan.Review);
        Assert.Equal(plan.Id, runtime.Request?.PlanId);
        Assert.Equal(plan.WorkItemRevision, runtime.Request?.WorkItemRevision);
        var expectedStage = automatic ? AgentStage.AutoApprovedExecution : AgentStage.Execution;
        Assert.Equal(expectedStage, promptBuilder.Stage);
        Assert.Equal(expectedStage, runtime.Request?.Stage);
        Assert.Same(current, runtime.Request?.ExecutionCatalog);
        Assert.Same(current, invocations.Started?.AvailableTools);
        Assert.True(invocations.Completed);
        Assert.Contains(events.Published, e => e is ExecutionCompleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EquivalentSchemaFormattingPreservesApproval(bool automatic)
    {
        var workItem = CreateApprovedWorkItem(automatic);
        var reformattedTool = PlannedTool with
        {
            InputSchemaJson = """
                {
                  "required": ["content"],
                  "properties": {"content": {"type": "string"}},
                  "type": "object"
                }
                """
        };
        var runtime = new RecordingRuntime();
        var builder = new StubPromptBuilder(Catalog("reformatted", ReadTool, reformattedTool));

        await CreateService(workItem, runtime, new RecordingInvocationService(), new RecordingEventPublisher(), builder)
            .ExecuteAsync(new WorkItemExecutionRequest(workItem.Id, "plan-1", workItem.Revision), CancellationToken.None);

        Assert.Equal(WorkItemStatus.Completed, workItem.Status);
        Assert.NotNull(runtime.Request);
    }

    [Theory]
    [InlineData(false, "removed")]
    [InlineData(true, "removed")]
    [InlineData(false, "disabled")]
    [InlineData(true, "disabled")]
    [InlineData(false, "schema")]
    [InlineData(true, "schema")]
    [InlineData(false, "name")]
    [InlineData(true, "name")]
    [InlineData(false, "description")]
    [InlineData(true, "description")]
    [InlineData(false, "invalid-schema")]
    [InlineData(true, "invalid-schema")]
    [InlineData(false, "missing-snapshot")]
    [InlineData(true, "missing-snapshot")]
    [InlineData(false, "disabled-snapshot")]
    [InlineData(true, "disabled-snapshot")]
    [InlineData(false, "duplicate")]
    [InlineData(true, "duplicate")]
    [InlineData(false, "read-removed")]
    [InlineData(true, "read-removed")]
    public async Task IncompatiblePlannedToolBlocksBeforeExecutionEvenWhenCatalogVersionMatches(
        bool automatic, string change)
    {
        var workItem = CreateApprovedWorkItem(automatic);
        var plan = workItem.FindCurrentPlan()!;
        var tools = new List<ToolCapability> { ReadTool, PlannedTool };
        switch (change)
        {
            case "removed": tools.Remove(PlannedTool); break;
            case "disabled": tools[1] = PlannedTool with { CanInvoke = false }; break;
            case "schema": tools[1] = PlannedTool with { InputSchemaJson = "{\"type\":\"object\",\"additionalProperties\":false}" }; break;
            case "name": tools[1] = PlannedTool with { Name = "replace_task" }; break;
            case "description": tools[1] = PlannedTool with { Description = "Replace all tasks." }; break;
            case "invalid-schema": tools[1] = PlannedTool with { InputSchemaJson = "not JSON" }; break;
            case "missing-snapshot": plan.ExecutionTools.Tools.RemoveAll(t => t.Id == ToolId); break;
            case "disabled-snapshot": plan.ExecutionTools.Tools.Single(t => t.Id == ToolId).CanInvoke = false; break;
            case "duplicate": tools.Add(PlannedTool); break;
            case "read-removed": tools.Remove(ReadTool); break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }
        var version = automatic ? "restricted-v1" : "regular-v1";
        var runtime = new RecordingRuntime();
        var invocations = new RecordingInvocationService();
        var events = new RecordingEventPublisher();

        await CreateService(workItem, runtime, invocations, events, new StubPromptBuilder(Catalog(version, [.. tools])))
            .ExecuteAsync(new WorkItemExecutionRequest(workItem.Id, plan.Id, workItem.Revision), CancellationToken.None);

        Assert.Equal(WorkItemStatus.NeedsAttention, workItem.Status);
        Assert.Null(runtime.Request);
        Assert.Null(invocations.Started);
        Assert.Equal(PlanReviewKind.Approved, plan.Review?.Kind);
        Assert.Equal(plan.Id, workItem.CurrentPlanId);
        var failure = Assert.IsType<ExecutionFailed>(Assert.Single(events.Published));
        Assert.Contains(change == "read-removed" ? ReadToolId : ToolId, failure.Reason);
    }

    private static DomainWorkItem CreateApprovedWorkItem(bool automatic)
    {
        var workItem = DomainWorkItem.CreateFromInbound("Create a follow-up task.", null, Now);
        var plan = new WorkItemPlan
        {
            Id = "plan-1", WorkItemId = workItem.Id, WorkItemRevision = workItem.Revision,
            PlanNumber = workItem.ReservePlanNumber(Now), Summary = "Create a follow-up task",
            ExecutionTools = AgentWorkflowSupport.CreatePlanToolSnapshot(Catalog("regular-v1", ReadTool, PlannedTool,
                new ToolCapability("wallet/get_records", "get_records", "Old unrelated tool.", "{}", true),
                new ToolCapability("wallet/removed_tool", "removed_tool", null, "{}", true))),
            Steps =
            [
                new PlanStep { Order = 1, Title = "Check duplicates", TechnicalDescription = "Find existing tasks.",
                    SuccessCriteria = "No duplicate exists.",
                    ToolCalls = [new PlanToolCall { ToolId = ReadToolId, ArgumentsJson = "{}" }] },
                new PlanStep { Order = 2, Title = "Create task", TechnicalDescription = "Add the task.",
                    HasExternalSideEffects = true, SuccessCriteria = "Task exists.",
                    ToolCalls = [new PlanToolCall { ToolId = ToolId, ArgumentsJson = "{\"content\":\"Follow up\"}" }] }
            ]
        };
        workItem.ApplyPlan(plan, Now);
        if (automatic)
        {
            workItem.RecordAutomaticApprovalAssessment(plan.Id, workItem.Revision, new AutomaticApprovalAssessment
            {
                Approved = true, Reason = "Approved bounded action.", InvocationId = "approval-invocation",
                ExecutionCatalogVersion = "restricted-v1", AssessedAt = Now
            });
        }
        else
        {
            workItem.Approve(plan.Id, workItem.Revision, Now);
        }
        workItem.DequeueEvents();
        return workItem;
    }

    private static ToolCatalogSnapshot Catalog(string version, params ToolCapability[] tools) => new(version, "{}",
        [new ToolServerCapability("test", "Test server", version, false, null, null, null, tools)]);

    private static WorkItemExecutionService CreateService(DomainWorkItem workItem, RecordingRuntime runtime,
        RecordingInvocationService invocations, RecordingEventPublisher events, StubPromptBuilder builder) => new(
        new WorkItemRepository(workItem), runtime, builder, invocations, events,
        TimeProvider.System, NullLogger<WorkItemExecutionService>.Instance);

    private sealed class RecordingRuntime : IAgentRuntime
    {
        public ExecutionRequest? Request { get; private set; }
        public Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new ExecutionResult(ExecutionDisposition.Succeeded, "Task created.", ["task-1"],
                new AgentReceipt("Test", "model", "completion", "{}")));
        }
        public Task<IntakeResult> ClassifyAsync(IntakeRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PlannerResult> PlanAsync(PlannerRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AutomaticApprovalResult> AssessAutomaticApprovalAsync(AutomaticApprovalRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubPromptBuilder(ToolCatalogSnapshot catalog) : IAgentPromptBuilder
    {
        public AgentStage? Stage { get; private set; }
        public Task<ExecutionAgentPrompt> BuildExecutionAsync(string workItemId, int workItemRevision, string planId,
            CancellationToken cancellationToken, AgentStage stage = AgentStage.Execution)
        {
            Stage = stage;
            return Task.FromResult(new ExecutionAgentPrompt("Execute the approved plan.", new AgentRunConfiguration("model", "high"), catalog));
        }
        public Task<IntakeAgentPrompt> BuildIntakeAsync(string inboundEventId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PlanningAgentPrompt> BuildPlanningAsync(string workItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AutomaticApprovalAgentPrompt> BuildAutomaticApprovalAsync(string workItemId, int workItemRevision, string planId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingInvocationService : IAgentInvocationService
    {
        public AgentInvocationStart? Started { get; private set; }
        public bool Completed { get; private set; }
        public Task<AgentInvocation> StartAsync(AgentInvocationStart request, CancellationToken cancellationToken)
        {
            Started = request;
            return Task.FromResult(new AgentInvocation { Id = "execution-invocation", Status = AgentInvocationStatus.Running });
        }
        public Task CompleteAsync(string agentInvocationId, AgentReceipt receipt, CancellationToken cancellationToken)
        {
            Completed = true;
            return Task.CompletedTask;
        }
        public Task FailAsync(string agentInvocationId, AgentInvocationStatus status, string error, CancellationToken cancellationToken) => throw new InvalidOperationException(error);
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

    private sealed class WorkItemRepository(DomainWorkItem workItem) : IRepository<DomainWorkItem>
    {
        public Task<DomainWorkItem?> GetByIdAsync(string id, CancellationToken cancellationToken = default, string? tenantId = null) => Task.FromResult(id == workItem.Id ? workItem : null);
        public Task UpdateAsync(DomainWorkItem entity, CancellationToken cancellationToken = default, string? tenantId = null) => Task.CompletedTask;
        public Task<DomainWorkItem> AddAsync(DomainWorkItem entity, CancellationToken cancellationToken = default, string? tenantId = null) => throw new NotSupportedException();
        public Task AddAsync(ICollection<DomainWorkItem> entities, CancellationToken cancellationToken = default, string? tenantId = null) => throw new NotSupportedException();
        public Task<IReadOnlyList<DomainWorkItem>> GetByIdsAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken = default, string? tenantId = null) => throw new NotSupportedException();
        public Task<DomainWorkItem?> GetBySpecAsync(ISpecification<DomainWorkItem> specification, CancellationToken cancellationToken = default, string? tenantId = null) => throw new NotSupportedException();
        public Task<TResult?> GetBySpecAsync<TResult>(ISpecification<DomainWorkItem, TResult> specification, CancellationToken cancellationToken = default, string? tenantId = null) => throw new NotSupportedException();
        public Task<IReadOnlyList<DomainWorkItem>> ListAsync(CancellationToken cancellationToken = default, string? tenantId = null) => throw new NotSupportedException();
        public Task<IReadOnlyList<DomainWorkItem>> ListAsync(IListSpecification<DomainWorkItem> specification, CancellationToken cancellationToken = default, string? tenantId = null) => throw new NotSupportedException();
        public Task<IReadOnlyList<TResult>> ListAsync<TResult>(IListSpecification<DomainWorkItem, TResult> specification, CancellationToken cancellationToken = default, string? tenantId = null) => throw new NotSupportedException();
        public Task<int> CountAsync(CancellationToken cancellationToken = default, string? tenantId = null) => throw new NotSupportedException();
        public Task GetAndUpdateAsync(string id, Func<DomainWorkItem, Task> updateMethod, CancellationToken cancellationToken = default, string? tenantId = null) => throw new NotSupportedException();
        public Task DeleteAsync(DomainWorkItem entity, CancellationToken cancellationToken = default, string? tenantId = null) => throw new NotSupportedException();
        public Task DeleteByIdAsync(string id, CancellationToken cancellationToken = default, string? tenantId = null) => throw new NotSupportedException();
        public Task DeleteRangeByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default, string? tenantId = null) => throw new NotSupportedException();
    }
}

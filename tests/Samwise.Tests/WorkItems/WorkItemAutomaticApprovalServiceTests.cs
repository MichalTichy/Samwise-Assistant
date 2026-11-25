using Microsoft.Extensions.Logging.Abstractions;
using Samwise.Domain.Agents;
using Samwise.Domain.WorkItems;
using Samwise.Logic.Agents;
using Samwise.Logic.Agents.Services;
using Samwise.Logic.Prompts.Services;
using Samwise.Logic.WorkItems.Services;
using Shared.Infrastructure.Persistence.Repositories;
using Shared.Infrastructure.Persistence.Specifications;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;

namespace Samwise.Tests.WorkItems;

public sealed class WorkItemAutomaticApprovalServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private const string ToolId = "todoist/create_task";
    private const string Schema = "{\"type\":\"object\"}";

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    public async Task OnlyAnEligibleApprovedPlanWithRestrictedToolsStartsExecution(
        bool eligible, bool agentApproves, bool approvalHasTools, bool expectExecution)
    {
        var workItem = CreateWorkItem(eligible);
        var plan = workItem.FindCurrentPlan()!;
        var runtime = new StubRuntime(agentApproves);
        var executor = new RecordingExecutionService();
        var service = CreateService(workItem, runtime, executor,
            includeRestrictedTool: true, approvalHasTools: approvalHasTools);

        await service.AssessAsync(workItem.Id, plan.Id, workItem.Revision, CancellationToken.None);

        Assert.Equal(expectExecution, executor.Request is not null);
        Assert.Equal(expectExecution ? WorkItemStatus.Approved : WorkItemStatus.AwaitingApproval, workItem.Status);
        Assert.Equal(expectExecution, plan.AutomaticApproval?.Approved);
        Assert.Equal(eligible, runtime.Called);
        if (expectExecution)
        {
            Assert.Equal(DomainWorkItem.AutomaticApprovalSource, plan.Review?.Source);
            Assert.Equal("restricted-v1", plan.AutomaticApproval?.ExecutionCatalogVersion);
        }
        else
        {
            Assert.Null(plan.Review);
            Assert.False(string.IsNullOrWhiteSpace(plan.AutomaticApproval?.Reason));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ToolMissingFromRestrictedProfileRequiresHumanApproval(bool approvalHasTools)
    {
        var workItem = CreateWorkItem(eligible: true);
        var plan = workItem.FindCurrentPlan()!;
        var runtime = new StubRuntime(approve: true);
        var executor = new RecordingExecutionService();
        var service = CreateService(workItem, runtime, executor,
            includeRestrictedTool: false, approvalHasTools: approvalHasTools);

        await service.AssessAsync(workItem.Id, plan.Id, workItem.Revision, CancellationToken.None);

        Assert.Equal(WorkItemStatus.AwaitingApproval, workItem.Status);
        Assert.Contains(ToolId, plan.AutomaticApproval?.Reason);
        Assert.False(runtime.Called);
        Assert.Null(executor.Request);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("schema")]
    [InlineData("name")]
    [InlineData("description")]
    public async Task ChangedRestrictedToolDefinitionCannotBeAutomaticallyApproved(string change)
    {
        var workItem = CreateWorkItem(eligible: true);
        var plan = workItem.FindCurrentPlan()!;
        var runtime = new StubRuntime(approve: true);
        var executor = new RecordingExecutionService();
        var tool = new ToolCapability(ToolId, "create_task", null, Schema, true);
        tool = change switch
        {
            "disabled" => tool with { CanInvoke = false },
            "schema" => tool with { InputSchemaJson = "{\"type\":\"object\",\"required\":[\"newArgument\"]}" },
            "name" => tool with { Name = "replace_task" },
            "description" => tool with { Description = "Replace all tasks." },
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
        var service = CreateService(workItem, runtime, executor, includeRestrictedTool: true, restrictedTool: tool);

        await service.AssessAsync(workItem.Id, plan.Id, workItem.Revision, CancellationToken.None);

        Assert.Equal(WorkItemStatus.AwaitingApproval, workItem.Status);
        Assert.Contains(ToolId, plan.AutomaticApproval?.Reason);
        Assert.Null(plan.Review);
        Assert.False(runtime.Called);
        Assert.Null(executor.Request);
    }

    [Fact]
    public async Task ApprovalProfileToolsDoNotReplaceRestrictedExecutionSchema()
    {
        var workItem = CreateWorkItem(eligible: true);
        var plan = workItem.FindCurrentPlan()!;
        var runtime = new StubRuntime(approve: true);
        var executor = new RecordingExecutionService();
        var service = CreateService(workItem, runtime, executor,
            includeRestrictedTool: true, approvalHasTools: true,
            restrictedSchema: "{\"type\":\"object\",\"required\":[\"newField\"]}");

        await service.AssessAsync(workItem.Id, plan.Id, workItem.Revision, CancellationToken.None);

        Assert.Equal(WorkItemStatus.AwaitingApproval, workItem.Status);
        Assert.Contains("with its approved definition", plan.AutomaticApproval?.Reason);
        Assert.False(runtime.Called);
        Assert.Null(executor.Request);
    }

    [Fact]
    public async Task NewContextDuringAssessmentPreventsExecutionOfTheSupersededPlan()
    {
        var workItem = CreateWorkItem(eligible: true);
        var plan = workItem.FindCurrentPlan()!;
        var revision = workItem.Revision;
        var runtime = new StubRuntime(approve: true,
            beforeDecision: () => workItem.AddContext("I already handled this in Teams.", Now.AddMinutes(1)));
        var executor = new RecordingExecutionService();
        var service = CreateService(workItem, runtime, executor,
            includeRestrictedTool: true, approvalHasTools: true);

        await service.AssessAsync(workItem.Id, plan.Id, revision, CancellationToken.None);

        Assert.True(runtime.Called);
        Assert.Equal(WorkItemStatus.Planning, workItem.Status);
        Assert.Equal(revision + 1, workItem.Revision);
        Assert.Null(workItem.CurrentPlanId);
        Assert.Null(plan.AutomaticApproval);
        Assert.Null(plan.Review);
        Assert.Null(executor.Request);
    }

    private static DomainWorkItem CreateWorkItem(bool eligible)
    {
        var workItem = DomainWorkItem.CreateFromInbound("Create a follow-up task.", null, Now);
        var number = workItem.ReservePlanNumber(Now);
        workItem.ApplyPlan(new WorkItemPlan
        {
            Id = "plan-1",
            WorkItemId = workItem.Id,
            WorkItemRevision = workItem.Revision,
            PlanNumber = number,
            Summary = "Create a follow-up task",
            Playbook = new PlanPlaybookSnapshot
            {
                Id = "todo",
                Name = "Todo creation",
                SuitableForAutomaticApproval = eligible
            },
            Steps = [new PlanStep
            {
                Order = 1,
                Title = "Create task",
                TechnicalDescription = "Add the task in Todoist.",
                SuccessCriteria = "Task exists.",
                ToolCalls = [new PlanToolCall { ToolId = ToolId, ArgumentsJson = "{}" }]
            }],
            ExecutionTools = new PlanToolCatalogSnapshot
            {
                CatalogVersion = "regular-v1",
                Tools = [new PlanToolCapability { Id = ToolId, Name = "create_task", InputSchemaJson = Schema, CanInvoke = true }]
            }
        }, Now);
        workItem.DequeueEvents();
        return workItem;
    }

    private static WorkItemAutomaticApprovalService CreateService(
        DomainWorkItem workItem,
        StubRuntime runtime,
        RecordingExecutionService executor,
        bool includeRestrictedTool,
        bool approvalHasTools = false,
        ToolCapability? restrictedTool = null,
        string restrictedSchema = Schema)
    {
        var restricted = new ToolCatalogSnapshot("restricted-v1", "{}", includeRestrictedTool
            ? [new ToolServerCapability("todoist", "Todoist", "1", false, null, null, null,
                [restrictedTool ?? new ToolCapability(ToolId, "create_task", null, restrictedSchema, true)])]
            : []);
        return new WorkItemAutomaticApprovalService(
            new WorkItemRepository(workItem), runtime,
            new StubToolCatalog(restricted, approvalHasTools),
            new StubPromptBuilder(restricted),
            new StubInvocationService(), executor,
            new StubEventPublisher(),
            new FixedTimeProvider(),
            NullLogger<WorkItemAutomaticApprovalService>.Instance);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now.AddMinutes(1);
    }

    private sealed class StubRuntime(bool approve, Action? beforeDecision = null) : IAgentRuntime
    {
        public bool Called { get; private set; }

        public Task<AutomaticApprovalResult> AssessAutomaticApprovalAsync(
            AutomaticApprovalRequest request, CancellationToken cancellationToken = default)
        {
            Called = true;
            beforeDecision?.Invoke();
            return Task.FromResult(new AutomaticApprovalResult(
                approve, approve ? "The task is specific and low risk." : "The user must confirm the details.",
                new AgentReceipt("Test", "model", "completion", "{}")));
        }

        public Task<IntakeResult> ClassifyAsync(IntakeRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<PlannerResult> PlanAsync(PlannerRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubToolCatalog(ToolCatalogSnapshot restricted, bool approvalHasTools) : IAgentToolCatalog
    {
        public Task<ToolCatalogSnapshot> GetToolsAsync(AgentStage stage, CancellationToken cancellationToken = default) =>
            Task.FromResult(stage == AgentStage.AutomaticApproval
                ? new ToolCatalogSnapshot("approval-v1", "{}", approvalHasTools
                    ? [new ToolServerCapability("todoist", "Todoist", "1", false, null, null, null,
                        [new ToolCapability(ToolId, "create_task", null, Schema, true)])]
                    : [])
                : restricted);
    }

    private sealed class StubPromptBuilder(ToolCatalogSnapshot restricted) : IAgentPromptBuilder
    {
        public Task<AutomaticApprovalAgentPrompt> BuildAutomaticApprovalAsync(
            string workItemId, int workItemRevision, string planId, CancellationToken cancellationToken) =>
            Task.FromResult(new AutomaticApprovalAgentPrompt(
                "Review this plan.", new AgentRunConfiguration("model", "medium"),
                new AgentRunConfiguration("executor-model", "medium"), restricted));
        public Task<IntakeAgentPrompt> BuildIntakeAsync(string inboundEventId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<PlanningAgentPrompt> BuildPlanningAsync(string workItemId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<ExecutionAgentPrompt> BuildExecutionAsync(string workItemId, int workItemRevision,
            string planId, CancellationToken cancellationToken, AgentStage stage = AgentStage.Execution) =>
            throw new NotSupportedException();
    }

    private sealed class StubInvocationService : IAgentInvocationService
    {
        public Task<AgentInvocation> StartAsync(AgentInvocationStart request, CancellationToken cancellationToken) =>
            Task.FromResult(new AgentInvocation { Id = "approval-invocation", Status = AgentInvocationStatus.Running });
        public Task CompleteAsync(string agentInvocationId, AgentReceipt receipt, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task FailAsync(string agentInvocationId, AgentInvocationStatus status,
            string error, CancellationToken cancellationToken) => throw new InvalidOperationException(error);
    }

    private sealed class RecordingExecutionService : IWorkItemExecutionService
    {
        public WorkItemExecutionRequest? Request { get; private set; }
        public Task ExecuteAsync(WorkItemExecutionRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.CompletedTask;
        }
    }

    private sealed class StubEventPublisher : IWorkItemEventPublisher
    {
        public Task PublishAsync(DomainWorkItem workItem)
        {
            workItem.DequeueEvents();
            return Task.CompletedTask;
        }
    }

    private sealed class WorkItemRepository(DomainWorkItem workItem) : IRepository<DomainWorkItem>
    {
        public Task<DomainWorkItem?> GetByIdAsync(string id, CancellationToken cancellationToken = default,
            string? tenantId = null) => Task.FromResult(id == workItem.Id ? workItem : null);
        public Task UpdateAsync(DomainWorkItem entity, CancellationToken cancellationToken = default,
            string? tenantId = null) => Task.CompletedTask;
        public Task<DomainWorkItem> AddAsync(DomainWorkItem entity, CancellationToken cancellationToken = default,
            string? tenantId = null) => throw new NotSupportedException();
        public Task AddAsync(ICollection<DomainWorkItem> entities, CancellationToken cancellationToken = default,
            string? tenantId = null) => throw new NotSupportedException();
        public Task<IReadOnlyList<DomainWorkItem>> GetByIdsAsync(IReadOnlyList<string> ids,
            CancellationToken cancellationToken = default, string? tenantId = null) => throw new NotSupportedException();
        public Task<DomainWorkItem?> GetBySpecAsync(ISpecification<DomainWorkItem> specification,
            CancellationToken cancellationToken = default, string? tenantId = null) => throw new NotSupportedException();
        public Task<TResult?> GetBySpecAsync<TResult>(ISpecification<DomainWorkItem, TResult> specification,
            CancellationToken cancellationToken = default, string? tenantId = null) => throw new NotSupportedException();
        public Task<IReadOnlyList<DomainWorkItem>> ListAsync(CancellationToken cancellationToken = default,
            string? tenantId = null) => throw new NotSupportedException();
        public Task<IReadOnlyList<DomainWorkItem>> ListAsync(IListSpecification<DomainWorkItem> specification,
            CancellationToken cancellationToken = default, string? tenantId = null) => throw new NotSupportedException();
        public Task<IReadOnlyList<TResult>> ListAsync<TResult>(IListSpecification<DomainWorkItem, TResult> specification,
            CancellationToken cancellationToken = default, string? tenantId = null) => throw new NotSupportedException();
        public Task<int> CountAsync(CancellationToken cancellationToken = default,
            string? tenantId = null) => throw new NotSupportedException();
        public Task GetAndUpdateAsync(string id, Func<DomainWorkItem, Task> updateMethod,
            CancellationToken cancellationToken = default, string? tenantId = null) => throw new NotSupportedException();
        public Task DeleteAsync(DomainWorkItem entity, CancellationToken cancellationToken = default,
            string? tenantId = null) => throw new NotSupportedException();
        public Task DeleteByIdAsync(string id, CancellationToken cancellationToken = default,
            string? tenantId = null) => throw new NotSupportedException();
        public Task DeleteRangeByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default,
            string? tenantId = null) => throw new NotSupportedException();
    }
}

using Samwise.Domain.Agents;
using Samwise.Domain.Inbox;
using Samwise.Domain.Playbooks;
using Samwise.Domain.WorkItems;
using Samwise.Logic.Agents;
using Samwise.Inputs.Manual.Events;
using Samwise.Logic.Prompts;
using Samwise.Logic.Prompts.Initialization;
using Samwise.Logic.Prompts.Services;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Persistence.Repositories;
using Shared.Infrastructure.Persistence.Specifications;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;

namespace Samwise.Tests.Prompts;

public sealed class AgentPromptBuilderTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 13, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task IntakePromptRetrievesItsInputCandidatesPlaybooksAndAllToolCatalogs()
    {
        var inboundEvent = new ManualInboundEvent
        {
            Id = "inbound-1",
            ManualInputId = "manual:1",
            Input = "Prepare the release.",
            SourceGuidance = "Treat release requests as work input.",
            Context =
            [
                new InboundEventContextItem
                {
                    SourceItemId = "earlier-message",
                    Content = "The release candidate passed QA.",
                    PreviouslyProcessed = true,
                    InboundEventId = "inbound-0",
                    WorkItemIds = ["work-item-0"]
                }
            ]
        };
        var workItem = DomainWorkItem.CreateFromInbound("Ship the previous release.", "Previous release", Now);
        var configuration = DefaultAgentConfiguration.Create(Now);
        configuration.Intake.Model = "intake-model";
        configuration.Intake.ReasoningEffort = "high";
        var playbook = new Playbook
        {
            Id = "marketing-unsubscribe",
            Name = "Marketing unsubscribe",
            Description = "Marketing messages with a safe unsubscribe method are actionable.",
            IsEnabled = true,
            UpdatedAt = Now
        };
        var catalogs = new RecordingToolCatalog();
        var builder = CreateBuilder(configuration, catalogs, [inboundEvent], [workItem], [playbook]);

        var result = await builder.BuildIntakeAsync(inboundEvent.Id, CancellationToken.None);

        Assert.Contains(inboundEvent.Input, result.Text, StringComparison.Ordinal);
        Assert.Contains(inboundEvent.SourceGuidance, result.Text, StringComparison.Ordinal);
        Assert.Contains("earlier-message", result.Text, StringComparison.Ordinal);
        Assert.Contains("work-item-0", result.Text, StringComparison.Ordinal);
        Assert.Contains(workItem.Id, result.Text, StringComparison.Ordinal);
        Assert.Contains(playbook.Id, result.Text, StringComparison.Ordinal);
        Assert.Contains(playbook.Description, result.Text, StringComparison.Ordinal);
        Assert.Contains(catalogs[AgentStage.Planning].RawJson, result.Text, StringComparison.Ordinal);
        Assert.Contains(catalogs[AgentStage.Execution].RawJson, result.Text, StringComparison.Ordinal);
        Assert.Equal("intake-model", result.Configuration.Model);
        Assert.Equal("high", result.Configuration.ReasoningEffort);
        Assert.Equal(catalogs[AgentStage.Intake], result.IntakeTools);
        Assert.Equal(
            [AgentStage.Intake, AgentStage.Planning, AgentStage.Execution],
            catalogs.RequestedStages.OrderBy(stage => stage));
    }

    [Fact]
    public async Task ExistingIntakeTemplateStillReceivesGuidanceAndContext()
    {
        var inboundEvent = new ManualInboundEvent
        {
            Id = "inbound-1",
            ManualInputId = "manual:1",
            Input = "Yes, do it.",
            SourceGuidance = "Treat this as a work conversation.",
            Context =
            [
                new InboundEventContextItem
                {
                    SourceItemId = "message-1",
                    Content = "Prepare the customer proposal.",
                    PreviouslyProcessed = true,
                    InboundEventId = "inbound-0",
                    WorkItemIds = ["work-item-0"]
                }
            ]
        };
        var configuration = DefaultAgentConfiguration.Create(Now);
        configuration.Intake.Prompt = "Classify this new input: {{input.text}}";
        var builder = CreateBuilder(configuration, new RecordingToolCatalog(), [inboundEvent], [], []);

        var result = await builder.BuildIntakeAsync(inboundEvent.Id, CancellationToken.None);

        Assert.Contains(inboundEvent.SourceGuidance, result.Text, StringComparison.Ordinal);
        Assert.Contains("Prepare the customer proposal.", result.Text, StringComparison.Ordinal);
        Assert.Contains("work-item-0", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlanningPromptRetrievesWorkItemPlaybooksAndExecutionTools()
    {
        var source = new WorkItemSourceReference
        {
            InboundEventId = "teams-transcript-event",
            Source = "Teams transcript",
            ExternalId = "scheduledMeeting:meeting-1:transcript-1",
            AddedAt = Now
        };
        var workItem = DomainWorkItem.CreateFromInbound("Process the invoice.", "Process invoice", Now, source);
        var playbook = new Playbook
        {
            Id = "invoice-processing",
            Name = "Invoice processing",
            Description = "Check duplicates and create only a draft entry.",
            IsEnabled = true,
            UpdatedAt = Now
        };
        var configuration = DefaultAgentConfiguration.Create(Now);
        var catalogs = new RecordingToolCatalog();
        var builder = CreateBuilder(configuration, catalogs, [], [workItem], [playbook]);

        var result = await builder.BuildPlanningAsync(workItem.Id, CancellationToken.None);

        Assert.Contains(workItem.Title, result.Text, StringComparison.Ordinal);
        Assert.Contains(playbook.Id, result.Text, StringComparison.Ordinal);
        Assert.Contains(playbook.Description, result.Text, StringComparison.Ordinal);
        Assert.Contains(catalogs[AgentStage.Execution].RawJson, result.Text, StringComparison.Ordinal);
        Assert.Contains(source.InboundEventId, result.Text, StringComparison.Ordinal);
        Assert.Contains(source.ExternalId, result.Text, StringComparison.Ordinal);
        Assert.Equal(playbook, Assert.Single(result.AvailablePlaybooks));
        Assert.Equal(catalogs[AgentStage.Planning], result.PlanningTools);
    }

    [Fact]
    public async Task ExecutionPromptRetrievesTheExactApprovedPlanAndCurrentTools()
    {
        var source = new WorkItemSourceReference
        {
            InboundEventId = "teams-message-event",
            Source = "Teams chat",
            ExternalId = "chat:chat-1:message-1:v1",
            AddedAt = Now
        };
        var workItem = DomainWorkItem.CreateFromInbound("Send the invoice.", "Send invoice", Now, source);
        var plan = new WorkItemPlan
        {
            Id = "plan-1",
            WorkItemId = workItem.Id,
            WorkItemRevision = workItem.Revision,
            PlanNumber = 1,
            Summary = "Send the approved invoice.",
            CreatedAt = Now
        };
        workItem.ApplyPlan(plan, Now);
        workItem.Approve(plan.Id, workItem.Revision, Now);
        var configuration = DefaultAgentConfiguration.Create(Now);
        var catalogs = new RecordingToolCatalog();
        var builder = CreateBuilder(configuration, catalogs, [], [workItem], []);

        var result = await builder.BuildExecutionAsync(
            workItem.Id,
            workItem.Revision,
            plan.Id,
            CancellationToken.None);

        Assert.Contains(workItem.Title, result.Text, StringComparison.Ordinal);
        Assert.Contains(plan.Id, result.Text, StringComparison.Ordinal);
        Assert.Contains(plan.Summary, result.Text, StringComparison.Ordinal);
        Assert.Contains(catalogs[AgentStage.Execution].RawJson, result.Text, StringComparison.Ordinal);
        Assert.Contains(source.InboundEventId, result.Text, StringComparison.Ordinal);
        Assert.Contains(source.ExternalId, result.Text, StringComparison.Ordinal);
        Assert.Equal(catalogs[AgentStage.Execution], result.ExecutionTools);
    }

    private static AgentPromptBuilder CreateBuilder(
        AgentConfiguration configuration,
        IAgentToolCatalog toolCatalog,
        IReadOnlyList<InboundEvent> inboundEvents,
        IReadOnlyList<DomainWorkItem> workItems,
        IReadOnlyList<Playbook> playbooks)
    {
        var placeholderCatalog = new AgentPromptPlaceholderCatalog();
        return new AgentPromptBuilder(
            new ReadOnlyRepository<InboundEvent>(inboundEvents),
            new ReadOnlyRepository<DomainWorkItem>(workItems),
            new ReadOnlyRepository<AgentConfiguration>([configuration]),
            new ReadOnlyRepository<Playbook>(playbooks),
            toolCatalog,
            placeholderCatalog,
            new PromptRenderer(placeholderCatalog),
            new FixedTimeProvider(Now));
    }

    private sealed class RecordingToolCatalog : IAgentToolCatalog
    {
        private readonly IReadOnlyDictionary<AgentStage, ToolCatalogSnapshot> catalogs =
            Enum.GetValues<AgentStage>().ToDictionary(
                stage => stage,
                stage => new ToolCatalogSnapshot(
                    $"{stage}-version",
                    $"{{\"stage\":\"{stage}\"}}",
                    []));

        public List<AgentStage> RequestedStages { get; } = [];

        public ToolCatalogSnapshot this[AgentStage stage] => catalogs[stage];

        public Task<ToolCatalogSnapshot> GetToolsAsync(
            AgentStage stage,
            CancellationToken cancellationToken = default)
        {
            RequestedStages.Add(stage);
            return Task.FromResult(catalogs[stage]);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
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

using Samwise.Domain.Agents;

namespace Samwise.Logic.Agents;

public enum IntakeDecision
{
    CreateWorkItem,
    AppendToWorkItem,
    Ignore,
    NeedsReview
}

public sealed record IntakeRequest(
    string InboundEventId,
    string Source,
    string RenderedPrompt,
    AgentRunConfiguration Configuration);

public sealed record IntakeResult(
    IntakeDecision Decision,
    string? ExistingWorkItemId,
    string? ProposedTitle,
    string? ProposedDescription,
    string Explanation,
    AgentReceipt Receipt);

public sealed record ToolCatalogSnapshot(
    string CatalogVersion,
    string RawJson,
    IReadOnlyList<ToolServerCapability> Servers)
{
    public IReadOnlyList<ToolCapability> Tools { get; } = Servers
        .SelectMany(server => server.Tools)
        .ToList();
}

public sealed record ToolServerCapability(
    string Id,
    string Name,
    string Version,
    bool Required,
    string? Title,
    string? Description,
    string? WebsiteUrl,
    IReadOnlyList<ToolCapability> Tools);

public sealed record ToolCapability(
    string Id,
    string Name,
    string? Description,
    string InputSchemaJson,
    bool CanInvoke);

public sealed record PlannerRequest(
    string WorkItemId,
    int WorkItemRevision,
    int PlanNumber,
    string RenderedPrompt,
    ToolCatalogSnapshot PlanningCatalog,
    ToolCatalogSnapshot ExecutionCatalog,
    AgentRunConfiguration Configuration);

public sealed record PlanDraft(
    string? PlaybookId,
    string Summary,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Risks,
    IReadOnlyList<PlanStepDraft> Steps);

public sealed record PlanStepDraft(
    int Order,
    string Title,
    string TechnicalDescription,
    IReadOnlyList<string> ToolIds,
    bool HasExternalSideEffects,
    string SuccessCriteria);

public sealed record PlannerResult(
    PlanDraft Plan,
    AgentReceipt Receipt);

public sealed record ExecutionRequest(
    string WorkItemId,
    int WorkItemRevision,
    string PlanId,
    string RenderedPrompt,
    ToolCatalogSnapshot ExecutionCatalog,
    AgentRunConfiguration Configuration);

public sealed record ExecutionResult(
    bool Succeeded,
    string Summary,
    IReadOnlyList<string> Outputs,
    AgentReceipt Receipt);

public sealed record AgentReceipt(
    string Provider,
    string? Model,
    string? CompletionId,
    string RawResponse);

public sealed record AgentRunConfiguration(
    string Model,
    string ReasoningEffort);

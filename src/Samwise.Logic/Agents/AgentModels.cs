namespace Samwise.Logic.Agents;

public enum IntakeDecision
{
    CreateTask,
    AppendToTask,
    Ignore,
    NeedsReview
}

public sealed record IntakeRequest(
    Guid InboundEventId,
    string Source,
    string RenderedPrompt);

public sealed record IntakeResult(
    IntakeDecision Decision,
    Guid? ExistingTaskId,
    string? ProposedTitle,
    string? ProposedDescription,
    string Explanation,
    AgentReceipt Receipt);

public sealed record ToolCatalogSnapshot(
    string CatalogVersion,
    string RawJson,
    IReadOnlyList<ToolCapability> Tools);

public sealed record ToolCapability(
    string Id,
    string Name,
    string? Description,
    string InputSchemaJson,
    bool CanInvoke);

public sealed record PlannerRequest(
    Guid TaskId,
    int TaskRevision,
    int PlanNumber,
    string RenderedPrompt,
    ToolCatalogSnapshot PlanningCatalog,
    ToolCatalogSnapshot ExecutionCatalog);

public sealed record PlanDraft(
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
    Guid TaskId,
    int TaskRevision,
    Guid PlanId,
    string RenderedPrompt,
    ToolCatalogSnapshot ExecutionCatalog);

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

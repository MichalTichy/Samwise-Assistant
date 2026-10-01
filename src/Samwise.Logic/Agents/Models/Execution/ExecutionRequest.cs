using Samwise.Domain.Agents;

namespace Samwise.Logic.Agents;

public sealed record ExecutionRequest(
    string WorkItemId,
    int WorkItemRevision,
    string PlanId,
    string RenderedPrompt,
    ToolCatalogSnapshot ExecutionCatalog,
    AgentRunConfiguration Configuration,
    AgentStage Stage = AgentStage.Execution);

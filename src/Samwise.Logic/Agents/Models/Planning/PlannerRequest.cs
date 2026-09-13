namespace Samwise.Logic.Agents;

public sealed record PlannerRequest(
    string WorkItemId,
    int WorkItemRevision,
    int PlanNumber,
    string RenderedPrompt,
    ToolCatalogSnapshot PlanningCatalog,
    ToolCatalogSnapshot ExecutionCatalog,
    AgentRunConfiguration Configuration);

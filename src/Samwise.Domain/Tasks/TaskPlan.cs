namespace Samwise.Domain.Tasks;

public sealed class TaskPlan
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public int TaskRevision { get; set; }
    public int PlanNumber { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> Assumptions { get; set; } = [];
    public List<string> Risks { get; set; } = [];
    public List<PlanStep> Steps { get; set; } = [];
    public string PlanningToolCatalogVersion { get; set; } = string.Empty;
    public string ExecutionToolCatalogVersion { get; set; } = string.Empty;
    public string PromptTemplateId { get; set; } = string.Empty;
    public string RenderedPrompt { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PlanStep
{
    public int Order { get; set; }
    public string Title { get; set; } = string.Empty;
    public string TechnicalDescription { get; set; } = string.Empty;
    public List<string> ToolIds { get; set; } = [];
    public bool HasExternalSideEffects { get; set; }
    public string SuccessCriteria { get; set; } = string.Empty;
}

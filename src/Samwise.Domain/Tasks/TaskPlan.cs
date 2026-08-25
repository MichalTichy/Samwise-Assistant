namespace Samwise.Domain.Tasks;

public sealed class TaskPlan
{
    public string Id { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public int TaskRevision { get; set; }
    public int PlanNumber { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> Assumptions { get; set; } = [];
    public List<string> Risks { get; set; } = [];
    public List<PlanStep> Steps { get; set; } = [];
    public PlanToolCatalogSnapshot PlanningTools { get; set; } = new();
    public PlanToolCatalogSnapshot ExecutionTools { get; set; } = new();
    public string PromptTemplateId { get; set; } = string.Empty;
    public DateTimeOffset PromptTemplateUpdatedAt { get; set; }
    public string PlanningInvocationId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PlanToolCatalogSnapshot
{
    public string CatalogVersion { get; set; } = string.Empty;
    public string RawJson { get; set; } = string.Empty;
    public List<PlanToolCapability> Tools { get; set; } = [];
}

public sealed class PlanToolCapability
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string InputSchemaJson { get; set; } = string.Empty;
    public bool CanInvoke { get; set; }
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

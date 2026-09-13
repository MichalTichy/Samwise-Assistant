namespace Samwise.Domain.WorkItems;

public sealed class PlanStep
{
    public int Order { get; set; }
    public string Title { get; set; } = string.Empty;
    public string TechnicalDescription { get; set; } = string.Empty;
    public List<PlanToolCall> ToolCalls { get; set; } = [];
    public bool HasExternalSideEffects { get; set; }
    public string SuccessCriteria { get; set; } = string.Empty;
}

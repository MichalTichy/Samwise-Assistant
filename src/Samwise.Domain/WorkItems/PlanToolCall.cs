namespace Samwise.Domain.WorkItems;

public sealed class PlanToolCall
{
    public string ToolId { get; set; } = string.Empty;
    public string ArgumentsJson { get; set; } = "{}";
}

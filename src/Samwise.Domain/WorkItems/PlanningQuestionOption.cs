namespace Samwise.Domain.WorkItems;

public sealed class PlanningQuestionOption
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
}

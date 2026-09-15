namespace Samwise.Domain.WorkItems;

public sealed class PlanDecisionSnapshot
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public DateTimeOffset AnsweredAt { get; set; }
}

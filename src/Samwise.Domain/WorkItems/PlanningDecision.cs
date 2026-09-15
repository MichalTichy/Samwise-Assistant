namespace Samwise.Domain.WorkItems;

public sealed class PlanningDecision
{
    public string Id { get; set; } = string.Empty;
    public string QuestionId { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string? OptionId { get; set; }
    public string Answer { get; set; } = string.Empty;
    public bool IsOther { get; set; }
    public DateTimeOffset AnsweredAt { get; set; }
}

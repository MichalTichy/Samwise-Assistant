namespace Samwise.Domain.WorkItems;

public sealed class PlanningQuestion
{
    public string Id { get; set; } = string.Empty;
    public int WorkItemRevision { get; set; }
    public int PlanningAttemptNumber { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string? Explanation { get; set; }
    public List<PlanningQuestionOption> Options { get; set; } = [];
    public string PlanningInvocationId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

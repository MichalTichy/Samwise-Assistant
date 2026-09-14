namespace Samwise.Domain.WorkItems;

public sealed class WorkItemCompletion
{
    public WorkItemCompletionKind Kind { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> Outputs { get; set; } = [];
    public string AgentInvocationId { get; set; } = string.Empty;
    public DateTimeOffset CompletedAt { get; set; }
}

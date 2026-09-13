namespace Samwise.Domain.WorkItems;

public sealed class WorkItemCompletion
{
    public string Summary { get; set; } = string.Empty;
    public List<string> Outputs { get; set; } = [];
    public string ExecutionInvocationId { get; set; } = string.Empty;
    public DateTimeOffset CompletedAt { get; set; }
}

namespace Samwise.Domain.WorkItems;

public sealed class AutomaticApprovalAssessment
{
    public bool Approved { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? InvocationId { get; set; }
    public string? ExecutionCatalogVersion { get; set; }
    public DateTimeOffset AssessedAt { get; set; }
}

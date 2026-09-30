namespace Samwise.Domain.WorkItems;

public sealed class PlanPlaybookSnapshot
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Applicability { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool SuitableForAutomaticApproval { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

namespace Samwise.Domain.WorkItems;

public sealed class PlanToolCapability
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string InputSchemaJson { get; set; } = string.Empty;
    public bool CanInvoke { get; set; }
}

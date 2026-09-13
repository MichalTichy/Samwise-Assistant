namespace Samwise.Domain.WorkItems;

public sealed class PlanToolCatalogSnapshot
{
    public string CatalogVersion { get; set; } = string.Empty;
    public string RawJson { get; set; } = string.Empty;
    public List<PlanToolCapability> Tools { get; set; } = [];
}

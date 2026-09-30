namespace Samwise.Domain.WorkItems;

public sealed class WorkItemPlan
{
    public string Id { get; set; } = string.Empty;
    public string WorkItemId { get; set; } = string.Empty;
    public int WorkItemRevision { get; set; }
    public int PlanNumber { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> Assumptions { get; set; } = [];
    public List<string> Risks { get; set; } = [];
    public List<PlanDecisionSnapshot> Decisions { get; set; } = [];
    public List<PlanStep> Steps { get; set; } = [];
    public PlanPlaybookSnapshot? Playbook { get; set; }
    public PlanToolCatalogSnapshot PlanningTools { get; set; } = new();
    public PlanToolCatalogSnapshot ExecutionTools { get; set; } = new();
    public string AgentConfigurationId { get; set; } = string.Empty;
    public DateTimeOffset AgentConfigurationUpdatedAt { get; set; }
    public string PlanningInvocationId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public PlanReview? Review { get; set; }
    public AutomaticApprovalAssessment? AutomaticApproval { get; set; }
}

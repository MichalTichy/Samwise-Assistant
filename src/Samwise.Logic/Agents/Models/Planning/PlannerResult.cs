namespace Samwise.Logic.Agents;

public sealed record PlannerResult(
    PlanningDisposition Disposition,
    string? ResolutionReason,
    PlanDraft Plan,
    AgentReceipt Receipt);

namespace Samwise.Logic.Agents;

public sealed record PlannerResult(
    PlanDraft Plan,
    AgentReceipt Receipt);

namespace Samwise.Logic.Agents;

public sealed record IntakeResult(
    IntakeDecision Decision,
    IReadOnlyList<IntakeWorkItemChange> WorkItems,
    string Explanation,
    AgentReceipt Receipt);

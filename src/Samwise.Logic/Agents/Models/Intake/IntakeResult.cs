namespace Samwise.Logic.Agents;

public sealed record IntakeResult(
    IntakeDisposition Disposition,
    IReadOnlyList<IntakeWorkItemChange> WorkItems,
    string Explanation,
    AgentReceipt Receipt);

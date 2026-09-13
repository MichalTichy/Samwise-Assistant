namespace Samwise.Logic.Agents;

public sealed record IntakeResult(
    IntakeDecision Decision,
    string? ExistingWorkItemId,
    string? ProposedTitle,
    string? ProposedDescription,
    string Explanation,
    AgentReceipt Receipt);

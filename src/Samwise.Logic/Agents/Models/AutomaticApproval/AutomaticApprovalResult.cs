namespace Samwise.Logic.Agents;

public sealed record AutomaticApprovalResult(
    bool Approve,
    string Reason,
    AgentReceipt Receipt);

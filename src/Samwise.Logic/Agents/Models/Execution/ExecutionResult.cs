namespace Samwise.Logic.Agents;

public sealed record ExecutionResult(
    bool Succeeded,
    string Summary,
    IReadOnlyList<string> Outputs,
    AgentReceipt Receipt);

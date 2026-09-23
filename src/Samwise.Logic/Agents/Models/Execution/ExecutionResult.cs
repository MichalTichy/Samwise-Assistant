namespace Samwise.Logic.Agents;

public sealed record ExecutionResult(
    ExecutionDisposition Disposition,
    string Summary,
    IReadOnlyList<string> Outputs,
    AgentReceipt Receipt);

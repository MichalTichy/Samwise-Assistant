namespace Samwise.Logic.Agents;

public sealed record AgentReceipt(
    string Provider,
    string? Model,
    string? CompletionId,
    string RawResponse);

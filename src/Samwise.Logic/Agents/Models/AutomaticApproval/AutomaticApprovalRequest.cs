namespace Samwise.Logic.Agents;

public sealed record AutomaticApprovalRequest(
    string RenderedPrompt,
    AgentRunConfiguration Configuration);

namespace Samwise.Logic.Agents;

public sealed record IntakeRequest(
    string InboundEventId,
    string Source,
    string RenderedPrompt,
    AgentRunConfiguration Configuration);

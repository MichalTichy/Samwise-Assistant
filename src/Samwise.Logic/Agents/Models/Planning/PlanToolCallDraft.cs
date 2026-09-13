namespace Samwise.Logic.Agents;

public sealed record PlanToolCallDraft(
    string ToolId,
    string ArgumentsJson);

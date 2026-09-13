namespace Samwise.Logic.Agents;

public sealed record PlanStepDraft(
    int Order,
    string Title,
    string TechnicalDescription,
    IReadOnlyList<PlanToolCallDraft> ToolCalls,
    bool HasExternalSideEffects,
    string SuccessCriteria);

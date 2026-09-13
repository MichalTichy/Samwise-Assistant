namespace Samwise.Logic.Agents;

public sealed record PlanStepDraft(
    int Order,
    string Title,
    string TechnicalDescription,
    IReadOnlyList<string> ToolIds,
    bool HasExternalSideEffects,
    string SuccessCriteria);

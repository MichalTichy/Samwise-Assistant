namespace Samwise.Logic.Agents;

public sealed record PlanDraft(
    string? PlaybookId,
    string Summary,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Risks,
    IReadOnlyList<PlanStepDraft> Steps);

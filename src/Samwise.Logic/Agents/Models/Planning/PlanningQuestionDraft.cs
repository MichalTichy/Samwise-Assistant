namespace Samwise.Logic.Agents;

public sealed record PlanningQuestionDraft(
    string Prompt,
    string? Explanation,
    IReadOnlyList<PlanningQuestionOptionDraft> Options);

using Samwise.Domain.WorkItems;

namespace Samwise.Logic.Agents;

public sealed record PlanDraft(
    string? PlaybookId,
    string Summary,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Risks,
    PlanCompletionMode CompletionMode,
    string? ExpectedFollowUp,
    IReadOnlyList<PlanStepDraft> Steps);

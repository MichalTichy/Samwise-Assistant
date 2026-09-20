namespace Samwise.Logic.Agents;

public sealed record IntakeWorkItemChange(
    IntakeWorkItemAction Action,
    string? ExistingWorkItemId,
    string? ProposedTitle,
    string RelevantText,
    string Explanation);

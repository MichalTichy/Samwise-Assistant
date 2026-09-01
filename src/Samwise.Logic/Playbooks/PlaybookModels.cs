namespace Samwise.Logic.Playbooks;

public sealed record PlaybookSaveResult(
    bool Succeeded,
    string? PlaybookId,
    IReadOnlyList<string> Errors);

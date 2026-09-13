namespace Samwise.Logic.Prompts;

public sealed record PromptRenderResult(bool Succeeded, string Text, IReadOnlyList<string> Errors);

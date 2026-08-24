using Samwise.Domain.Prompts;

namespace Samwise.Logic.Prompts;

public sealed record PromptPlaceholder(
    string Key,
    string Label,
    string Description,
    string Example);

public sealed record PromptTemplateEditor(
    string Id,
    PromptStage Stage,
    string Name,
    string Content,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<PromptPlaceholder> Placeholders);

public sealed record PromptSaveResult(bool Succeeded, IReadOnlyList<string> Errors)
{
    public static PromptSaveResult Success { get; } = new(true, []);
}

public sealed record PromptRenderResult(bool Succeeded, string Text, IReadOnlyList<string> Errors);

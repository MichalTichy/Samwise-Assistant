using Samwise.Domain.Agents;

namespace Samwise.Logic.Prompts;

public sealed record PromptPlaceholder(
    string Key,
    string Label,
    string Description,
    string Example);

public sealed record AgentStageConfigurationEditor(
    AgentStage Stage,
    string Name,
    string Prompt,
    string Model,
    string ReasoningEffort,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<PromptPlaceholder> Placeholders);

public sealed record AgentStageConfigurationSaveResult(bool Succeeded, IReadOnlyList<string> Errors)
{
    public static AgentStageConfigurationSaveResult Success { get; } = new(true, []);
}

public sealed record PromptRenderResult(bool Succeeded, string Text, IReadOnlyList<string> Errors);

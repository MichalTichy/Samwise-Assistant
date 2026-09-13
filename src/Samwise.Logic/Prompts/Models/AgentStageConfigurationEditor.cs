using Samwise.Domain.Agents;

namespace Samwise.Logic.Prompts;

public sealed record AgentStageConfigurationEditor(
    AgentStage Stage,
    string Name,
    string Prompt,
    string Model,
    string ReasoningEffort,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<PromptPlaceholder> Placeholders);

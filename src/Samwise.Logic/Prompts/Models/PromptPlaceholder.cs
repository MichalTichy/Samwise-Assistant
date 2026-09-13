using Samwise.Domain.Agents;

namespace Samwise.Logic.Prompts;

public sealed record PromptPlaceholder(
    string Key,
    string Label,
    string Description,
    string Example,
    IReadOnlyList<AgentStage> Stages);

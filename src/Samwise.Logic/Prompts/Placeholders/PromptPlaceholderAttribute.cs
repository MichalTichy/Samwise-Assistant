using Samwise.Domain.Agents;

namespace Samwise.Logic.Prompts;

[AttributeUsage(AttributeTargets.Field)]
public sealed class PromptPlaceholderAttribute(
    string label,
    string description,
    string example,
    params AgentStage[] stages) : Attribute
{
    public string Label { get; } = label;
    public string Description { get; } = description;
    public string Example { get; } = example;
    public IReadOnlyList<AgentStage> Stages { get; } = stages;
}

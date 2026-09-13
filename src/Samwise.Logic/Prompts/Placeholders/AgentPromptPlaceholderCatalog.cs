using System.Reflection;
using Samwise.Domain.Agents;

namespace Samwise.Logic.Prompts;

internal sealed class AgentPromptPlaceholderCatalog : IAgentPromptPlaceholderCatalog
{
    private static readonly IReadOnlyList<PromptPlaceholder> Placeholders = BuildPlaceholders();

    public IReadOnlyList<PromptPlaceholder> GetForStage(AgentStage stage) =>
        Placeholders.Where(placeholder => placeholder.Stages.Contains(stage)).ToList();

    private static IReadOnlyList<PromptPlaceholder> BuildPlaceholders()
    {
        var placeholders = typeof(AgentPromptPlaceholders)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field =>
            {
                var metadata = field.GetCustomAttribute<PromptPlaceholderAttribute>()
                    ?? throw new InvalidOperationException(
                        $"Prompt placeholder '{field.Name}' has no metadata attribute.");
                if (metadata.Stages.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Prompt placeholder '{field.Name}' is not assigned to an agent stage.");
                }

                return new PromptPlaceholder(
                    (string)field.GetRawConstantValue()!,
                    metadata.Label,
                    metadata.Description,
                    metadata.Example,
                    metadata.Stages);
            })
            .ToList();

        var duplicate = placeholders
            .GroupBy(placeholder => placeholder.Key, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Prompt placeholder key '{duplicate.Key}' is declared more than once.");
        }

        return placeholders;
    }
}

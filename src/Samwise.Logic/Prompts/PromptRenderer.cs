using System.Text.RegularExpressions;
using Samwise.Domain.Agents;

namespace Samwise.Logic.Prompts;

internal sealed partial class PromptRenderer(
    IAgentPromptPlaceholderCatalog placeholderCatalog) : IPromptRenderer
{
    public IReadOnlyList<string> Validate(AgentStage stage, string template)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(template))
        {
            errors.Add("The prompt cannot be empty.");
            return errors;
        }

        var allowed = placeholderCatalog.GetForStage(stage)
            .Select(item => item.Key)
            .ToHashSet(StringComparer.Ordinal);
        var matches = PlaceholderRegex().Matches(template);
        foreach (var unknown in matches
                     .Select(match => match.Groups["key"].Value)
                     .Where(key => !allowed.Contains(key))
                     .Distinct(StringComparer.Ordinal))
        {
            errors.Add($"Unknown placeholder '{{{{{unknown}}}}}'.");
        }

        var withoutValidTokens = PlaceholderRegex().Replace(template, string.Empty);
        if (withoutValidTokens.Contains("{{", StringComparison.Ordinal) ||
            withoutValidTokens.Contains("}}", StringComparison.Ordinal))
        {
            errors.Add("A placeholder is malformed. Use the exact form {{placeholder}}.");
        }

        return errors;
    }

    public PromptRenderResult Render(
        AgentStage stage,
        string template,
        IReadOnlyDictionary<string, string?> values)
    {
        var errors = Validate(stage, template).ToList();
        if (errors.Count > 0)
        {
            return new PromptRenderResult(false, string.Empty, errors);
        }

        var rendered = PlaceholderRegex().Replace(template, match =>
        {
            var key = match.Groups["key"].Value;
            if (!values.TryGetValue(key, out var value))
            {
                errors.Add($"No value was supplied for '{{{{{key}}}}}'.");
                return string.Empty;
            }

            return value ?? string.Empty;
        });

        return errors.Count == 0
            ? new PromptRenderResult(true, rendered, [])
            : new PromptRenderResult(false, string.Empty, errors);
    }

    [GeneratedRegex(@"\{\{\s*(?<key>[a-z0-9._-]+)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();
}

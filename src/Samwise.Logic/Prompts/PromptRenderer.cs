using System.Text.RegularExpressions;
using Samwise.Logic.Prompts;
using Samwise.Domain.Prompts;

namespace Samwise.Logic.Prompts;

internal sealed partial class PromptRenderer : IPromptRenderer
{
    private static readonly IReadOnlyDictionary<PromptStage, IReadOnlyList<PromptPlaceholder>> Definitions =
        new Dictionary<PromptStage, IReadOnlyList<PromptPlaceholder>>
        {
            [PromptStage.Intake] =
            [
                new("input.text", "Input text", "Normalized text received from the source.", "Please send the revised offer."),
                new("active_tasks", "Active tasks", "Compact JSON list of possible existing tasks.", "[{...}]"),
                new("current_time", "Current time", "Current UTC time in ISO 8601 format.", "2026-08-17T14:30:00Z")
            ],
            [PromptStage.Planning] =
            [
                new("task.id", "Task ID", "Stable Samwise task identifier.", "8b07..."),
                new("task.title", "Task title", "Short task title.", "Prepare revised offer"),
                new("task.description", "Task description", "The complete current task statement.", "Prepare the offer..."),
                new("task.revision", "Task revision", "Revision the generated plan must match.", "2"),
                new("task.comments", "Task comments", "User context and planning feedback as Markdown.", "- Add delivery dates"),
                new("previous_plan", "Previous plan", "Previous plan JSON, or an empty string.", "{...}"),
                new("rejection_feedback", "Rejection feedback", "Latest user feedback, or an empty string.", "Use the new price list"),
                new("execution_tools", "Execution tools", "Execution Gateway tool catalog as untrusted JSON data.", "{...}"),
                new("current_time", "Current time", "Current UTC time in ISO 8601 format.", "2026-08-17T14:30:00Z")
            ],
            [PromptStage.Execution] =
            [
                new("task.id", "Task ID", "Stable Samwise task identifier.", "8b07..."),
                new("task.title", "Task title", "Short task title.", "Prepare revised offer"),
                new("task.description", "Task description", "The complete approved task statement.", "Prepare the offer..."),
                new("task.revision", "Task revision", "Revision approved by the user.", "2"),
                new("task.comments", "Task comments", "User context as Markdown.", "- Add delivery dates"),
                new("approved_plan", "Approved plan", "Exact approved plan JSON.", "{...}"),
                new("execution_tools", "Execution tools", "Current execution tool catalog as untrusted JSON data.", "{...}"),
                new("current_time", "Current time", "Current UTC time in ISO 8601 format.", "2026-08-17T14:30:00Z")
            ]
        };

    public IReadOnlyList<PromptPlaceholder> GetPlaceholders(PromptStage stage) => Definitions[stage];

    public IReadOnlyList<string> Validate(PromptStage stage, string template)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(template))
        {
            errors.Add("The prompt cannot be empty.");
            return errors;
        }

        var allowed = Definitions[stage].Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
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
        PromptStage stage,
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

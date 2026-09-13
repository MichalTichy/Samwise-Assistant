using System.Reflection;
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

public static class AgentPromptPlaceholders
{
    [PromptPlaceholder(
        "Input text",
        "Normalized text received from the source.",
        "Please send the revised offer.",
        AgentStage.Intake)]
    public const string InputText = "input.text";

    [PromptPlaceholder(
        "Active work items",
        "Compact JSON list of possible existing work items.",
        "[{...}]",
        AgentStage.Intake)]
    public const string ActiveWorkItems = "active_work_items";

    [PromptPlaceholder(
        "Planning tools",
        "Planning Gateway MCP catalog as untrusted JSON data.",
        "{...}",
        AgentStage.Intake)]
    public const string PlanningTools = "planning_tools";

    [PromptPlaceholder(
        "Execution tools",
        "Execution Gateway MCP catalog as untrusted JSON data.",
        "{...}",
        AgentStage.Intake,
        AgentStage.Planning,
        AgentStage.Execution)]
    public const string ExecutionTools = "execution_tools";

    [PromptPlaceholder(
        "Current time",
        "Current UTC time in ISO 8601 format.",
        "2026-08-17T14:30:00Z",
        AgentStage.Intake,
        AgentStage.Planning,
        AgentStage.Execution)]
    public const string CurrentTime = "current_time";

    [PromptPlaceholder(
        "Work item ID",
        "Stable Samwise work item identifier.",
        "8b07...",
        AgentStage.Planning,
        AgentStage.Execution)]
    public const string WorkItemId = "work_item.id";

    [PromptPlaceholder(
        "Work item title",
        "Short work item title.",
        "Prepare revised offer",
        AgentStage.Planning,
        AgentStage.Execution)]
    public const string WorkItemTitle = "work_item.title";

    [PromptPlaceholder(
        "Work item description",
        "The complete current work item statement.",
        "Prepare the offer...",
        AgentStage.Planning,
        AgentStage.Execution)]
    public const string WorkItemDescription = "work_item.description";

    [PromptPlaceholder(
        "Work item revision",
        "Revision the generated or approved plan must match.",
        "2",
        AgentStage.Planning,
        AgentStage.Execution)]
    public const string WorkItemRevision = "work_item.revision";

    [PromptPlaceholder(
        "Work item comments",
        "User context and planning feedback as Markdown.",
        "- Add delivery dates",
        AgentStage.Planning,
        AgentStage.Execution)]
    public const string WorkItemComments = "work_item.comments";

    [PromptPlaceholder(
        "Previous plan",
        "Previous plan JSON, or an empty string.",
        "{...}",
        AgentStage.Planning)]
    public const string PreviousPlan = "previous_plan";

    [PromptPlaceholder(
        "Rejection feedback",
        "Latest user feedback, or an empty string.",
        "Use the new price list",
        AgentStage.Planning)]
    public const string RejectionFeedback = "rejection_feedback";

    [PromptPlaceholder(
        "Playbooks",
        "Enabled playbooks as trusted planning guidance in JSON format.",
        "[{...}]",
        AgentStage.Planning)]
    public const string Playbooks = "playbooks";

    [PromptPlaceholder(
        "Approved plan",
        "Exact approved plan JSON.",
        "{...}",
        AgentStage.Execution)]
    public const string ApprovedPlan = "approved_plan";
}

public interface IAgentPromptPlaceholderCatalog
{
    IReadOnlyList<PromptPlaceholder> GetForStage(AgentStage stage);
}

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

using System.Text.Json;
using Samwise.Domain.Inbox;
using Samwise.Domain.WorkItems;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;

namespace Samwise.Logic.Agents;

internal static class AgentWorkflowSupport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static IReadOnlyDictionary<string, string?> BuildIntakeValues(
        InboundEvent inboundEvent,
        IReadOnlyList<DomainWorkItem> candidates,
        DateTimeOffset now) =>
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["input.text"] = inboundEvent.Text,
            ["active_work_items"] = JsonSerializer.Serialize(
                candidates.Select(workItem => new
                {
                    id = workItem.Id,
                    title = workItem.Title,
                    description = Truncate(workItem.Description, 1_000),
                    status = workItem.Status.ToString(),
                    revision = workItem.Revision,
                    updated_at = workItem.UpdatedAt
                }),
                JsonOptions),
            ["current_time"] = now.ToString("O")
        };

    public static IReadOnlyDictionary<string, string?> BuildPlanningValues(
        DomainWorkItem workItem,
        IReadOnlyList<WorkItemComment> comments,
        WorkItemPlan? previousPlan,
        ToolCatalogSnapshot executionCatalog,
        DateTimeOffset now) =>
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["work_item.id"] = workItem.Id,
            ["work_item.title"] = workItem.Title,
            ["work_item.description"] = workItem.Description,
            ["work_item.revision"] = workItem.Revision.ToString(),
            ["work_item.comments"] = FormatComments(comments),
            ["previous_plan"] = previousPlan is null ? string.Empty : JsonSerializer.Serialize(previousPlan, JsonOptions),
            ["rejection_feedback"] = workItem.LatestPlanningFeedback,
            ["execution_tools"] = executionCatalog.RawJson,
            ["current_time"] = now.ToString("O")
        };

    public static IReadOnlyDictionary<string, string?> BuildExecutionValues(
        DomainWorkItem workItem,
        IReadOnlyList<WorkItemComment> comments,
        WorkItemPlan plan,
        ToolCatalogSnapshot executionCatalog,
        DateTimeOffset now) =>
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["work_item.id"] = workItem.Id,
            ["work_item.title"] = workItem.Title,
            ["work_item.description"] = workItem.Description,
            ["work_item.revision"] = workItem.Revision.ToString(),
            ["work_item.comments"] = FormatComments(comments),
            ["approved_plan"] = JsonSerializer.Serialize(plan, JsonOptions),
            ["execution_tools"] = executionCatalog.RawJson,
            ["current_time"] = now.ToString("O")
        };

    public static string FormatExecutionResult(ExecutionResult result) => result.Outputs.Count == 0
        ? result.Summary
        : string.Concat(
            result.Summary,
            Environment.NewLine,
            Environment.NewLine,
            "Outputs:",
            Environment.NewLine,
            string.Join(Environment.NewLine, result.Outputs));

    public static PlanToolCatalogSnapshot CreatePlanToolSnapshot(ToolCatalogSnapshot catalog) =>
        new()
        {
            CatalogVersion = catalog.CatalogVersion,
            RawJson = catalog.RawJson,
            Tools = catalog.Tools.Select(tool => new PlanToolCapability
            {
                Id = tool.Id,
                Name = tool.Name,
                Description = tool.Description,
                InputSchemaJson = tool.InputSchemaJson,
                CanInvoke = tool.CanInvoke
            }).ToList()
        };

    public static void ValidatePlan(PlanDraft plan, ToolCatalogSnapshot executionCatalog)
    {
        if (string.IsNullOrWhiteSpace(plan.Summary) || plan.Steps.Count == 0)
        {
            throw new InvalidOperationException("The planner returned an empty plan.");
        }

        var expectedOrder = 1;
        var invocableTools = executionCatalog.Tools
            .Where(tool => tool.CanInvoke)
            .Select(tool => tool.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var step in plan.Steps.OrderBy(step => step.Order))
        {
            if (step.Order != expectedOrder++)
            {
                throw new InvalidOperationException("Plan step numbers must be consecutive and start at one.");
            }

            if (string.IsNullOrWhiteSpace(step.Title) ||
                string.IsNullOrWhiteSpace(step.TechnicalDescription) ||
                string.IsNullOrWhiteSpace(step.SuccessCriteria))
            {
                throw new InvalidOperationException(
                    "Every plan step needs a title, technical description, and success criterion.");
            }

            var unavailableTool = step.ToolIds.FirstOrDefault(toolId => !invocableTools.Contains(toolId));
            if (unavailableTool is not null)
            {
                throw new InvalidOperationException(
                    $"The plan references execution tool '{unavailableTool}', which is not currently invocable.");
            }
        }
    }

    private static string FormatComments(IReadOnlyList<WorkItemComment> comments) => comments.Count == 0
        ? string.Empty
        : string.Join(Environment.NewLine, comments.Select(comment => $"- [{comment.AuthorKind}] {comment.Body}"));

    private static string Truncate(string value, int maximumLength) => value.Length <= maximumLength
        ? value
        : value[..maximumLength] + "…";
}

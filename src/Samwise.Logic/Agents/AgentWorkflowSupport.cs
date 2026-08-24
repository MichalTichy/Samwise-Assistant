using System.Text.Json;
using Samwise.Domain.Inbox;
using Samwise.Domain.Tasks;
using DomainTask = Samwise.Domain.Tasks.Task;

namespace Samwise.Logic.Agents;

internal static class AgentWorkflowSupport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static IReadOnlyDictionary<string, string?> BuildIntakeValues(
        InboundEvent inboundEvent,
        IReadOnlyList<DomainTask> candidates,
        DateTimeOffset now) =>
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["input.text"] = inboundEvent.Text,
            ["active_tasks"] = JsonSerializer.Serialize(
                candidates.Select(task => new
                {
                    id = task.Id,
                    title = task.Title,
                    description = Truncate(task.Description, 1_000),
                    status = task.Status.ToString(),
                    revision = task.Revision,
                    updated_at = task.UpdatedAt
                }),
                JsonOptions),
            ["current_time"] = now.ToString("O")
        };

    public static IReadOnlyDictionary<string, string?> BuildPlanningValues(
        DomainTask task,
        IReadOnlyList<TaskComment> comments,
        TaskPlan? previousPlan,
        ToolCatalogSnapshot executionCatalog,
        DateTimeOffset now) =>
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["task.id"] = task.Id,
            ["task.title"] = task.Title,
            ["task.description"] = task.Description,
            ["task.revision"] = task.Revision.ToString(),
            ["task.comments"] = FormatComments(comments),
            ["previous_plan"] = previousPlan is null ? string.Empty : JsonSerializer.Serialize(previousPlan, JsonOptions),
            ["rejection_feedback"] = task.LatestPlanningFeedback,
            ["execution_tools"] = executionCatalog.RawJson,
            ["current_time"] = now.ToString("O")
        };

    public static IReadOnlyDictionary<string, string?> BuildExecutionValues(
        DomainTask task,
        IReadOnlyList<TaskComment> comments,
        TaskPlan plan,
        ToolCatalogSnapshot executionCatalog,
        DateTimeOffset now) =>
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["task.id"] = task.Id,
            ["task.title"] = task.Title,
            ["task.description"] = task.Description,
            ["task.revision"] = task.Revision.ToString(),
            ["task.comments"] = FormatComments(comments),
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

    private static string FormatComments(IReadOnlyList<TaskComment> comments) => comments.Count == 0
        ? string.Empty
        : string.Join(Environment.NewLine, comments.Select(comment => $"- [{comment.AuthorKind}] {comment.Body}"));

    private static string Truncate(string value, int maximumLength) => value.Length <= maximumLength
        ? value
        : value[..maximumLength] + "…";
}

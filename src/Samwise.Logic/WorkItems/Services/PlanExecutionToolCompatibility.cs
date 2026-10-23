using System.Text.Json;
using Samwise.Domain.WorkItems;
using Samwise.Logic.Agents;

namespace Samwise.Logic.WorkItems.Services;

internal static class PlanExecutionToolCompatibility
{
    public static string? FindIncompatibleTool(WorkItemPlan plan, ToolCatalogSnapshot currentCatalog)
    {
        foreach (var toolId in plan.Steps.SelectMany(step => step.ToolCalls)
                     .Select(call => call.ToolId).Distinct(StringComparer.Ordinal))
        {
            var approved = plan.ExecutionTools.Tools.Where(tool => tool.Id == toolId).ToArray();
            var current = currentCatalog.Tools.Where(tool => tool.Id == toolId).ToArray();
            if (approved.Length != 1 || current.Length != 1 ||
                !approved[0].CanInvoke || !current[0].CanInvoke ||
                !string.Equals(approved[0].Name, current[0].Name, StringComparison.Ordinal) ||
                !string.Equals(approved[0].Description, current[0].Description, StringComparison.Ordinal) ||
                !SchemasMatch(approved[0].InputSchemaJson, current[0].InputSchemaJson))
            {
                return toolId;
            }
        }

        return null;
    }

    private static bool SchemasMatch(string approved, string current)
    {
        try
        {
            using var approvedSchema = JsonDocument.Parse(approved);
            using var currentSchema = JsonDocument.Parse(current);
            return JsonElement.DeepEquals(approvedSchema.RootElement, currentSchema.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

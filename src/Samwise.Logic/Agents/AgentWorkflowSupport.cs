using System.Text.Json;
using Samwise.Domain.Playbooks;
using Samwise.Domain.WorkItems;

namespace Samwise.Logic.Agents;

internal static class AgentWorkflowSupport
{
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

    public static PlanPlaybookSnapshot? ResolvePlaybookSnapshot(
        string? playbookId,
        IReadOnlyList<Playbook> availablePlaybooks)
    {
        if (string.IsNullOrWhiteSpace(playbookId))
        {
            return null;
        }

        var playbook = availablePlaybooks.SingleOrDefault(
            candidate => string.Equals(candidate.Id, playbookId.Trim(), StringComparison.Ordinal));
        if (playbook is null)
        {
            throw new InvalidOperationException(
                $"The planner selected playbook '{playbookId}', which is not currently enabled.");
        }

        return new PlanPlaybookSnapshot
        {
            Id = playbook.Id,
            Name = playbook.Name,
            Description = playbook.Description,
            UpdatedAt = playbook.UpdatedAt
        };
    }

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

            foreach (var toolCall in step.ToolCalls)
            {
                if (string.IsNullOrWhiteSpace(toolCall.ToolId))
                {
                    throw new InvalidOperationException("A suggested tool call is missing its tool ID.");
                }

                if (!invocableTools.Contains(toolCall.ToolId))
                {
                    throw new InvalidOperationException(
                        $"The plan suggests unavailable execution tool '{toolCall.ToolId}'.");
                }

                if (string.IsNullOrWhiteSpace(toolCall.ArgumentsJson))
                {
                    throw new InvalidOperationException(
                        $"Suggested parameters for '{toolCall.ToolId}' cannot be empty.");
                }

                try
                {
                    using var arguments = JsonDocument.Parse(toolCall.ArgumentsJson);
                    if (arguments.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        throw new InvalidOperationException(
                            $"Suggested parameters for '{toolCall.ToolId}' must be a JSON object.");
                    }
                }
                catch (JsonException exception)
                {
                    throw new InvalidOperationException(
                        $"Suggested parameters for '{toolCall.ToolId}' are not valid JSON.",
                        exception);
                }
            }
        }
    }
}

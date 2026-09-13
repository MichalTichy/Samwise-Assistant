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

        if (plan.CompletionMode == PlanCompletionMode.WaitForFollowUp &&
            string.IsNullOrWhiteSpace(plan.ExpectedFollowUp))
        {
            throw new InvalidOperationException(
                "A plan that waits for follow-up must describe the expected follow-up.");
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
}

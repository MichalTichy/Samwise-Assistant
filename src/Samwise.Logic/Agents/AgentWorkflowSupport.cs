using System.Text.Json;
using Samwise.Domain.Playbooks;
using Samwise.Domain.WorkItems;

namespace Samwise.Logic.Agents;

internal static class AgentWorkflowSupport
{
    public static void ValidateIntakeResult(IntakeResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Explanation))
        {
            throw new InvalidOperationException("The intake result needs an explanation.");
        }

        if (result.Decision == IntakeDecision.ApplyChanges && result.WorkItems.Count == 0)
        {
            throw new InvalidOperationException("An applied intake result must contain at least one work item change.");
        }

        if (result.Decision != IntakeDecision.ApplyChanges && result.WorkItems.Count > 0)
        {
            throw new InvalidOperationException("Ignored or review-required input cannot contain work item changes.");
        }

        foreach (var change in result.WorkItems)
        {
            if (string.IsNullOrWhiteSpace(change.RelevantText) || string.IsNullOrWhiteSpace(change.Explanation))
            {
                throw new InvalidOperationException(
                    "Every intake work item change needs relevant input text and an explanation.");
            }

            switch (change.Action)
            {
                case IntakeWorkItemAction.Create when
                    string.IsNullOrWhiteSpace(change.ProposedTitle) ||
                    !string.IsNullOrWhiteSpace(change.ExistingWorkItemId):
                    throw new InvalidOperationException(
                        "A create change needs a proposed title and cannot reference an existing work item.");
                case IntakeWorkItemAction.Append when
                    string.IsNullOrWhiteSpace(change.ExistingWorkItemId) ||
                    !string.IsNullOrWhiteSpace(change.ProposedTitle):
                    throw new InvalidOperationException(
                        "An append change needs an existing work item ID and cannot propose a title.");
            }
        }

        var duplicateAppendTarget = result.WorkItems
            .Where(change => change.Action == IntakeWorkItemAction.Append)
            .Select(change => change.ExistingWorkItemId!.Trim())
            .GroupBy(id => id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateAppendTarget is not null)
        {
            throw new InvalidOperationException(
                $"Intake returned more than one update for work item '{duplicateAppendTarget.Key}'.");
        }
    }

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
            Applicability = playbook.Applicability,
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

    public static void ValidatePlanningQuestion(PlanningQuestionDraft? question)
    {
        if (question is null || string.IsNullOrWhiteSpace(question.Prompt))
        {
            throw new InvalidOperationException("The planner requested user input without a question.");
        }

        if (question.Options.Count is < 2 or > 6)
        {
            throw new InvalidOperationException("A planning question must offer between two and six answers.");
        }

        if (question.Options.Any(option => string.IsNullOrWhiteSpace(option.Label)) ||
            question.Options.Select(option => option.Label.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != question.Options.Count)
        {
            throw new InvalidOperationException("Planning question answers must have distinct, non-empty labels.");
        }
    }
}

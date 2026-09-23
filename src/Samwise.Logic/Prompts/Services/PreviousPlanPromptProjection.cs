using System.Text.Json;
using Samwise.Domain.WorkItems;

namespace Samwise.Logic.Prompts.Services;

internal static class PreviousPlanPromptProjection
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string Serialize(WorkItemPlan plan) =>
        JsonSerializer.Serialize(new
        {
            id = plan.Id,
            work_item_revision = plan.WorkItemRevision,
            plan_number = plan.PlanNumber,
            summary = plan.Summary,
            assumptions = plan.Assumptions,
            risks = plan.Risks,
            decisions = plan.Decisions,
            playbook = plan.Playbook is null ? null : new { plan.Playbook.Id, plan.Playbook.Name },
            steps = plan.Steps.Select(step => new
            {
                order = step.Order,
                title = step.Title,
                technical_description = step.TechnicalDescription,
                tool_calls = step.ToolCalls.Select(call => new
                {
                    tool_id = call.ToolId,
                    arguments_json = call.ArgumentsJson
                }),
                has_external_side_effects = step.HasExternalSideEffects,
                success_criteria = step.SuccessCriteria
            }),
            review = plan.Review is null
                ? null
                : new
                {
                    kind = plan.Review.Kind.ToString(),
                    feedback = plan.Review.Feedback,
                    reviewed_at = plan.Review.ReviewedAt
                }
        }, JsonOptions);
}

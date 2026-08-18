using System.Text.Json;
using Samwise.Logic.Agents;

namespace Samwise.Infrastructure.AgentRuntimes.Agents;

internal sealed class DemoAgentRuntime : IAgentRuntime
{
    private static readonly ToolCatalogSnapshot EmptyCatalog = new(
        "demo:no-tools",
        "{\"object\":\"list\",\"catalog_version\":\"demo:no-tools\",\"data\":[]}",
        []);

    public Task<IntakeResult> ClassifyAsync(
        IntakeRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new
        {
            decision = "create_task",
            explanation = "The development runtime treats non-empty manual input as a new actionable task."
        };

        return Task.FromResult(new IntakeResult(
            IntakeDecision.CreateTask,
            null,
            null,
            null,
            result.explanation,
            new AgentReceipt("Development demo", "deterministic", null, JsonSerializer.Serialize(result))));
    }

    public Task<ToolCatalogSnapshot> GetPlanningToolsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(EmptyCatalog);

    public Task<ToolCatalogSnapshot> GetExecutionToolsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(EmptyCatalog);

    public Task<PlannerResult> PlanAsync(PlannerRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = new PlanDraft(
            "Review the task, carry out the requested work, and verify the result.",
            ["The task text contains enough information to start."],
            ["This is a development plan; no external tools are configured."],
            [
                new PlanStepDraft(
                    1,
                    "Review the task",
                    "Read the complete task context and identify the concrete output and constraints.",
                    [],
                    false,
                    "The requested outcome and constraints are explicit."),
                new PlanStepDraft(
                    2,
                    "Perform the work",
                    "Produce the requested result using only the approved context and available capabilities.",
                    [],
                    false,
                    "A complete result is produced."),
                new PlanStepDraft(
                    3,
                    "Verify and report",
                    "Check the result against the task and report what was completed or what needs attention.",
                    [],
                    false,
                    "The result is checked and clearly reported.")
            ]);

        var raw = JsonSerializer.Serialize(plan);
        return Task.FromResult(new PlannerResult(
            plan,
            new AgentReceipt("Development demo", "deterministic", null, raw)));
    }

    public Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string summary = "The development runtime simulated the approved execution. Configure the Codex Gateway before using real tools.";
        return Task.FromResult(new ExecutionResult(
            true,
            summary,
            [summary],
            new AgentReceipt("Development demo", "deterministic", null, JsonSerializer.Serialize(new { succeeded = true, summary }))));
    }
}

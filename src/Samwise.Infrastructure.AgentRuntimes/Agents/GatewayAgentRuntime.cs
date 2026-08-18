using System.Text.Json;
using System.Text.Json.Serialization;
using Samwise.Infrastructure.AgentGateway.Agents;
using Samwise.Logic.Agents;

namespace Samwise.Infrastructure.AgentRuntimes.Agents;

internal sealed class GatewayAgentRuntime(ICodexGatewayClient gateway) : IAgentRuntime
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<IntakeResult> ClassifyAsync(
        IntakeRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await gateway.RunStructuredAsync<IntakeWireResult>(
            GatewayStage.Intake,
            request.RenderedPrompt,
            SerializerOptions,
            cancellationToken);
        var result = response.Result;

        return new IntakeResult(
            ParseIntakeDecision(result.Decision),
            ParseExistingTaskId(result.ExistingTaskId),
            result.ProposedTitle,
            result.ProposedDescription,
            result.Explanation,
            CreateReceipt(response));
    }

    public async Task<ToolCatalogSnapshot> GetPlanningToolsAsync(CancellationToken cancellationToken = default) =>
        MapToolCatalog(await gateway.GetToolsAsync(GatewayStage.Planning, cancellationToken));

    public async Task<ToolCatalogSnapshot> GetExecutionToolsAsync(CancellationToken cancellationToken = default) =>
        MapToolCatalog(await gateway.GetToolsAsync(GatewayStage.Execution, cancellationToken));

    public async Task<PlannerResult> PlanAsync(
        PlannerRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await gateway.RunStructuredAsync<PlannerWireResult>(
            GatewayStage.Planning,
            request.RenderedPrompt,
            SerializerOptions,
            cancellationToken);
        var result = response.Result;
        var plan = new PlanDraft(
            result.Summary,
            result.Assumptions,
            result.Risks,
            result.Steps.Select(step => new PlanStepDraft(
                step.Order,
                step.Title,
                step.TechnicalDescription,
                step.ToolIds,
                step.HasExternalSideEffects,
                step.SuccessCriteria)).ToList());

        return new PlannerResult(plan, CreateReceipt(response));
    }

    public async Task<ExecutionResult> ExecuteAsync(
        ExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await gateway.RunStructuredAsync<ExecutionWireResult>(
            GatewayStage.Execution,
            request.RenderedPrompt,
            SerializerOptions,
            cancellationToken);
        var result = response.Result;

        return new ExecutionResult(
            result.Succeeded,
            result.Summary,
            result.Outputs,
            CreateReceipt(response));
    }

    private static AgentReceipt CreateReceipt<T>(GatewayStructuredResponse<T> response) =>
        new("Codex Gateway", response.Model, response.CompletionId, response.RawResponse);

    private static ToolCatalogSnapshot MapToolCatalog(GatewayToolCatalogSnapshot catalog) =>
        new(
            catalog.Version,
            catalog.RawJson,
            catalog.Tools.Select(tool => new ToolCapability(
                tool.Id,
                tool.Name,
                tool.Description,
                tool.InputSchemaJson,
                tool.CanInvoke)).ToList());

    private static IntakeDecision ParseIntakeDecision(string value) => value
        .Replace("_", string.Empty, StringComparison.Ordinal)
        .Replace("-", string.Empty, StringComparison.Ordinal)
        .ToLowerInvariant() switch
    {
        "createtask" => IntakeDecision.CreateTask,
        "appendtotask" => IntakeDecision.AppendToTask,
        "ignore" => IntakeDecision.Ignore,
        "needsreview" => IntakeDecision.NeedsReview,
        _ => throw new InvalidOperationException($"Unknown intake decision '{value}'.")
    };

    private static Guid? ParseExistingTaskId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("The intake result contains an invalid existing task ID.");
    }

    private sealed class PlannerWireResult
    {
        public required string Summary { get; init; }
        public required List<string> Assumptions { get; init; }
        public required List<string> Risks { get; init; }
        public required List<PlanStepWireResult> Steps { get; init; }
    }

    private sealed class IntakeWireResult
    {
        public required string Decision { get; init; }

        [JsonRequired]
        public string? ExistingTaskId { get; init; }

        [JsonRequired]
        public string? ProposedTitle { get; init; }

        [JsonRequired]
        public string? ProposedDescription { get; init; }

        public required string Explanation { get; init; }
    }

    private sealed class PlanStepWireResult
    {
        public required int Order { get; init; }
        public required string Title { get; init; }
        public required string TechnicalDescription { get; init; }
        public required List<string> ToolIds { get; init; }
        public required bool HasExternalSideEffects { get; init; }
        public required string SuccessCriteria { get; init; }
    }

    private sealed class ExecutionWireResult
    {
        public required bool Succeeded { get; init; }
        public required string Summary { get; init; }
        public required List<string> Outputs { get; init; }
    }
}

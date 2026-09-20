using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using Samwise.Domain.Agents;
using Samwise.Logic.Agents;

namespace Samwise.Infrastructure.AgentRuntimes.Agents;

internal sealed class OpenAiAgentRuntime : IAgentRuntime
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private readonly AgentEndpointOptions endpoints;

    public OpenAiAgentRuntime(IOptions<AgentEndpointOptions> options)
    {
        endpoints = options.Value;
        ValidateProfile(endpoints.Intake, "Intake");
        ValidateProfile(endpoints.Planning, "Planning");
        ValidateProfile(endpoints.Execution, "Execution");
    }

    public async Task<IntakeResult> ClassifyAsync(
        IntakeRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await RunStructuredAsync<IntakeWireResult>(
            AgentStage.Intake,
            request.Configuration.Model,
            request.Configuration.ReasoningEffort,
            request.RenderedPrompt,
            cancellationToken);
        var result = response.Result;

        return new IntakeResult(
            ParseIntakeDisposition(result.Disposition),
            result.WorkItems.Select(workItem => new IntakeWorkItemChange(
                ParseIntakeWorkItemAction(workItem.Action),
                ParseExistingWorkItemId(workItem.ExistingWorkItemId),
                workItem.ProposedTitle,
                workItem.RelevantText,
                workItem.Explanation)).ToList(),
            result.Explanation,
            CreateReceipt(response));
    }

    public async Task<PlannerResult> PlanAsync(
        PlannerRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await RunStructuredAsync<PlannerWireResult>(
            AgentStage.Planning,
            request.Configuration.Model,
            request.Configuration.ReasoningEffort,
            request.RenderedPrompt,
            cancellationToken);
        var result = response.Result;
        var plan = new PlanDraft(
            result.PlaybookId,
            result.Summary,
            result.Assumptions,
            result.Risks,
            result.Steps.Select(step => new PlanStepDraft(
                step.Order,
                step.Title,
                step.TechnicalDescription,
                step.ToolCalls.Select(toolCall => new PlanToolCallDraft(
                    toolCall.ToolId,
                    toolCall.ArgumentsJson)).ToList(),
                step.HasExternalSideEffects,
                step.SuccessCriteria)).ToList());

        return new PlannerResult(
            ParsePlanningDisposition(result.Disposition),
            ParseResolutionReason(result.ResolutionReason),
            plan,
            result.Question is null
                ? null
                : new PlanningQuestionDraft(
                    result.Question.Prompt,
                    result.Question.Explanation,
                    result.Question.Options.Select(option => new PlanningQuestionOptionDraft(
                        option.Label,
                        option.Description)).ToList()),
            CreateReceipt(response));
    }

    public async Task<ExecutionResult> ExecuteAsync(
        ExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await RunStructuredAsync<ExecutionWireResult>(
            AgentStage.Execution,
            request.Configuration.Model,
            request.Configuration.ReasoningEffort,
            request.RenderedPrompt,
            cancellationToken);
        var result = response.Result;

        return new ExecutionResult(
            result.Succeeded,
            result.Summary,
            result.Outputs,
            CreateReceipt(response));
    }

    private async Task<StructuredResponse<TResponse>> RunStructuredAsync<TResponse>(
        AgentStage stage,
        string model,
        string reasoningEffort,
        string prompt,
        CancellationToken cancellationToken)
    {
        var (profile, agentName, retryCount) = stage switch
        {
            AgentStage.Intake => (endpoints.Intake, "SamwiseIntake", 2),
            AgentStage.Planning => (endpoints.Planning, "SamwisePlanner", 2),
            AgentStage.Execution => (endpoints.Execution, "SamwiseExecutor", 0),
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
        };
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException($"The {stage} agent model is not configured.");
        }

        if (string.IsNullOrWhiteSpace(reasoningEffort))
        {
            throw new InvalidOperationException($"The {stage} agent reasoning effort is not configured.");
        }

        using var client = CreateChatClient(profile, model.Trim(), endpoints.RequestTimeout, retryCount);
        var agent = new ChatClientAgent(client, name: agentName);
        var options = CreateChatOptions<TResponse>(reasoningEffort.Trim());
        var response = await agent.RunAsync<TResponse>(
            prompt,
            session: null,
            serializerOptions: SerializerOptions,
            options: new ChatClientAgentRunOptions(options),
            cancellationToken: cancellationToken);

        return new StructuredResponse<TResponse>(
            response.Result,
            model.Trim(),
            response.ResponseId,
            response.Text);
    }

    private static IChatClient CreateChatClient(
        AgentEndpointProfileOptions profile,
        string model,
        TimeSpan timeout,
        int retryCount)
    {
        var client = new OpenAIClient(
            new ApiKeyCredential(profile.ApiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(profile.BaseAddress, UriKind.Absolute),
                NetworkTimeout = timeout,
                RetryPolicy = new ClientRetryPolicy(retryCount)
            });
        return client.GetChatClient(model).AsIChatClient();
    }

    private static ChatOptions CreateChatOptions<TResponse>(string effort)
    {
        var responseFormat = ChatResponseFormat.ForJsonSchema<TResponse>(SerializerOptions);

        return new ChatOptions
        {
            ResponseFormat = responseFormat,
            RawRepresentationFactory = _ => new OpenAI.Chat.ChatCompletionOptions
            {
#pragma warning disable OPENAI001 // The raw extensible value preserves endpoint-advertised efforts the MEAI enum does not know.
                ReasoningEffortLevel = new OpenAI.Chat.ChatReasoningEffortLevel(effort),
#pragma warning restore OPENAI001
                ResponseFormat = OpenAI.Chat.ChatResponseFormat.CreateJsonSchemaFormat(
                    responseFormat.SchemaName,
                    BinaryData.FromString(
                        responseFormat.Schema?.GetRawText()
                            ?? throw new InvalidOperationException("The structured response format has no JSON schema.")),
                    responseFormat.SchemaDescription,
                    jsonSchemaIsStrict: true)
            }
        };
    }

    private static void ValidateProfile(AgentEndpointProfileOptions profile, string name)
    {
        if (!Uri.TryCreate(profile.BaseAddress, UriKind.Absolute, out var baseAddress) ||
            !baseAddress.AbsolutePath.EndsWith("/v1/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"AgentEndpoints:{name}:BaseAddress must be absolute and end in /v1/.");
        }

        if (string.IsNullOrWhiteSpace(profile.ApiKey))
        {
            throw new InvalidOperationException($"AgentEndpoints:{name} requires ApiKey.");
        }
    }

    private static AgentReceipt CreateReceipt<T>(StructuredResponse<T> response) =>
        new("OpenAI Chat Completions", response.Model, response.CompletionId, response.RawResponse);

    private static IntakeDisposition ParseIntakeDisposition(string value) => value
        .Replace("_", string.Empty, StringComparison.Ordinal)
        .Replace("-", string.Empty, StringComparison.Ordinal)
        .ToLowerInvariant() switch
    {
        "apply" => IntakeDisposition.Apply,
        "ignore" => IntakeDisposition.Ignore,
        "needsreview" => IntakeDisposition.NeedsReview,
        _ => throw new InvalidOperationException($"Unknown intake disposition '{value}'.")
    };

    private static IntakeWorkItemAction ParseIntakeWorkItemAction(string value) => value
        .Replace("_", string.Empty, StringComparison.Ordinal)
        .Replace("-", string.Empty, StringComparison.Ordinal)
        .ToLowerInvariant() switch
    {
        "create" => IntakeWorkItemAction.Create,
        "append" => IntakeWorkItemAction.Append,
        _ => throw new InvalidOperationException($"Unknown intake work item action '{value}'.")
    };

    private static string? ParseExistingWorkItemId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static PlanningDisposition ParsePlanningDisposition(string value) => value
        .Replace("_", string.Empty, StringComparison.Ordinal)
        .Replace("-", string.Empty, StringComparison.Ordinal)
        .ToLowerInvariant() switch
    {
        "planrequired" => PlanningDisposition.PlanRequired,
        "userinputrequired" => PlanningDisposition.UserInputRequired,
        "alreadyresolved" => PlanningDisposition.AlreadyResolved,
        "nolongerrelevant" => PlanningDisposition.NoLongerRelevant,
        _ => throw new InvalidOperationException($"Unknown planning disposition '{value}'.")
    };

    private static string? ParseResolutionReason(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : value.Trim();

    private sealed class PlannerWireResult
    {
        public required string Disposition { get; init; }

        [JsonRequired]
        public string? ResolutionReason { get; init; }

        [JsonRequired]
        public string? PlaybookId { get; init; }

        [JsonRequired]
        public PlanningQuestionWireResult? Question { get; init; }

        public required string Summary { get; init; }
        public required List<string> Assumptions { get; init; }
        public required List<string> Risks { get; init; }
        public required List<PlanStepWireResult> Steps { get; init; }
    }

    private sealed class PlanningQuestionWireResult
    {
        public required string Prompt { get; init; }

        [JsonRequired]
        public string? Explanation { get; init; }

        public required List<PlanningQuestionOptionWireResult> Options { get; init; }
    }

    private sealed class PlanningQuestionOptionWireResult
    {
        public required string Label { get; init; }

        [JsonRequired]
        public string? Description { get; init; }
    }

    private sealed class IntakeWireResult
    {
        public required string Disposition { get; init; }

        public required List<IntakeWorkItemChangeWireResult> WorkItems { get; init; }

        public required string Explanation { get; init; }
    }

    private sealed class IntakeWorkItemChangeWireResult
    {
        public required string Action { get; init; }

        [JsonRequired]
        public string? ExistingWorkItemId { get; init; }

        [JsonRequired]
        public string? ProposedTitle { get; init; }

        [JsonRequired]
        public required string RelevantText { get; init; }

        public required string Explanation { get; init; }
    }

    private sealed class PlanStepWireResult
    {
        public required int Order { get; init; }
        public required string Title { get; init; }
        public required string TechnicalDescription { get; init; }
        public required List<PlanToolCallWireResult> ToolCalls { get; init; }
        public required bool HasExternalSideEffects { get; init; }
        public required string SuccessCriteria { get; init; }
    }

    private sealed class PlanToolCallWireResult
    {
        public required string ToolId { get; init; }
        public required string ArgumentsJson { get; init; }
    }

    private sealed class ExecutionWireResult
    {
        public required bool Succeeded { get; init; }
        public required string Summary { get; init; }
        public required List<string> Outputs { get; init; }
    }

    private sealed record StructuredResponse<T>(
        T Result,
        string Model,
        string? CompletionId,
        string RawResponse);
}

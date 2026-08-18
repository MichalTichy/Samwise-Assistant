using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using Samwise.Logic.Agents;

namespace Samwise.Infrastructure.AgentGateway.Agents;

internal sealed class GatewayAgentRuntime : IAgentRuntime, IDisposable
{
    internal const string IntakeHttpClientName = "Samwise.Gateway.Intake";
    internal const string PlanningHttpClientName = "Samwise.Gateway.Planning";
    internal const string ExecutionHttpClientName = "Samwise.Gateway.Execution";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly GatewayProfileOptions intakeProfile;
    private readonly GatewayProfileOptions planningProfile;
    private readonly GatewayProfileOptions executionProfile;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IChatClient intakeClient;
    private readonly IChatClient planningClient;
    private readonly IChatClient executionClient;
    private readonly ChatClientAgent intakeAgent;
    private readonly ChatClientAgent planningAgent;
    private readonly ChatClientAgent executionAgent;

    public GatewayAgentRuntime(
        IOptions<CodexGatewayOptions> options,
        IHttpClientFactory httpClientFactory)
    {
        this.httpClientFactory = httpClientFactory;
        intakeProfile = options.Value.Intake;
        planningProfile = options.Value.Planning;
        executionProfile = options.Value.Execution;
        ValidateProfile(intakeProfile, "Intake");
        ValidateProfile(planningProfile, "Planning");
        ValidateProfile(executionProfile, "Execution");
        intakeClient = CreateChatClient(intakeProfile, options.Value.RequestTimeout, retryCount: 2);
        planningClient = CreateChatClient(planningProfile, options.Value.RequestTimeout, retryCount: 2);
        executionClient = CreateChatClient(executionProfile, options.Value.RequestTimeout, retryCount: 0);
        intakeAgent = new ChatClientAgent(intakeClient, name: "SamwiseIntake");
        planningAgent = new ChatClientAgent(planningClient, name: "SamwisePlanner");
        executionAgent = new ChatClientAgent(executionClient, name: "SamwiseExecutor");
    }

    public async Task<IntakeResult> ClassifyAsync(
        IntakeRequest request,
        CancellationToken cancellationToken = default)
    {
        var options = CreateChatOptions<IntakeWireResult>(intakeProfile.ReasoningEffort);
        var response = await intakeAgent.RunAsync<IntakeWireResult>(
            request.RenderedPrompt,
            session: null,
            serializerOptions: SerializerOptions,
            options: new ChatClientAgentRunOptions(options),
            cancellationToken: cancellationToken);
        var result = response.Result;
        var decision = ParseIntakeDecision(result.Decision);

        return new IntakeResult(
            decision,
            ParseExistingTaskId(result.ExistingTaskId),
            result.ProposedTitle,
            result.ProposedDescription,
            result.Explanation,
            new AgentReceipt(
                "Codex Gateway",
                intakeProfile.Model,
                response.ResponseId,
                response.Text));
    }

    public Task<ToolCatalogSnapshot> GetPlanningToolsAsync(CancellationToken cancellationToken = default) =>
        GetToolsAsync(PlanningHttpClientName, cancellationToken);

    public Task<ToolCatalogSnapshot> GetExecutionToolsAsync(CancellationToken cancellationToken = default) =>
        GetToolsAsync(ExecutionHttpClientName, cancellationToken);

    public async Task<PlannerResult> PlanAsync(
        PlannerRequest request,
        CancellationToken cancellationToken = default)
    {
        var options = CreateChatOptions<PlannerWireResult>(planningProfile.ReasoningEffort);
        var response = await planningAgent.RunAsync<PlannerWireResult>(
            request.RenderedPrompt,
            session: null,
            serializerOptions: SerializerOptions,
            options: new ChatClientAgentRunOptions(options),
            cancellationToken: cancellationToken);
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

        return new PlannerResult(
            plan,
            new AgentReceipt(
                "Codex Gateway",
                planningProfile.Model,
                response.ResponseId,
                response.Text));
    }

    public async Task<ExecutionResult> ExecuteAsync(
        ExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var options = CreateChatOptions<ExecutionWireResult>(executionProfile.ReasoningEffort);
        var response = await executionAgent.RunAsync<ExecutionWireResult>(
            request.RenderedPrompt,
            session: null,
            serializerOptions: SerializerOptions,
            options: new ChatClientAgentRunOptions(options),
            cancellationToken: cancellationToken);
        var result = response.Result;

        return new ExecutionResult(
            result.Succeeded,
            result.Summary,
            result.Outputs,
            new AgentReceipt(
                "Codex Gateway",
                executionProfile.Model,
                response.ResponseId,
                response.Text));
    }

    public void Dispose()
    {
        intakeClient.Dispose();
        planningClient.Dispose();
        executionClient.Dispose();
    }

    private async Task<ToolCatalogSnapshot> GetToolsAsync(
        string clientName,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(clientName);
        using var response = await client.GetAsync("tools?detail=full", cancellationToken);
        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;
        var catalogVersion = root.GetProperty("catalog_version").GetString()
            ?? throw new InvalidOperationException("The Gateway tool catalog has no version.");

        var tools = new List<ToolCapability>();
        foreach (var server in root.GetProperty("data").EnumerateArray())
        {
            foreach (var tool in server.GetProperty("tools").EnumerateArray())
            {
                tools.Add(new ToolCapability(
                    tool.GetProperty("id").GetString() ?? throw new InvalidOperationException("A Gateway tool has no ID."),
                    tool.GetProperty("name").GetString() ?? string.Empty,
                    tool.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.String
                        ? description.GetString()
                        : null,
                    tool.GetProperty("input_schema").GetRawText(),
                    tool.GetProperty("can_invoke").GetBoolean()));
            }
        }

        return new ToolCatalogSnapshot(catalogVersion, raw, tools);
    }

    private static IChatClient CreateChatClient(
        GatewayProfileOptions profile,
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
        return client.GetChatClient(profile.Model).AsIChatClient();
    }

    private static ChatOptions CreateChatOptions<TResponse>(string effort)
    {
        var responseFormat = Microsoft.Extensions.AI.ChatResponseFormat.ForJsonSchema<TResponse>(
            SerializerOptions);

        return new ChatOptions
        {
            Reasoning = new ReasoningOptions { Effort = ParseReasoningEffort(effort) },
            ResponseFormat = responseFormat,
            RawRepresentationFactory = _ => new OpenAI.Chat.ChatCompletionOptions
            {
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

    private static ReasoningEffort ParseReasoningEffort(string value) => value.ToLowerInvariant() switch
    {
        "none" => ReasoningEffort.None,
        "low" => ReasoningEffort.Low,
        "medium" => ReasoningEffort.Medium,
        "high" => ReasoningEffort.High,
        "xhigh" => ReasoningEffort.ExtraHigh,
        _ => throw new InvalidOperationException($"Unsupported reasoning effort '{value}'.")
    };

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

    private static void ValidateProfile(GatewayProfileOptions profile, string name)
    {
        if (!Uri.TryCreate(profile.BaseAddress, UriKind.Absolute, out var baseAddress) ||
            !baseAddress.AbsolutePath.EndsWith("/v1/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"CodexGateway:{name}:BaseAddress must be absolute and end in /v1/.");
        }

        if (string.IsNullOrWhiteSpace(profile.ApiKey) || string.IsNullOrWhiteSpace(profile.Model))
        {
            throw new InvalidOperationException($"CodexGateway:{name} requires ApiKey and Model.");
        }
    }

    internal static void ConfigureHttpClient(HttpClient client, GatewayProfileOptions profile, TimeSpan timeout)
    {
        if (Uri.TryCreate(profile.BaseAddress, UriKind.Absolute, out var baseAddress))
        {
            client.BaseAddress = baseAddress;
        }

        client.Timeout = timeout;
        if (!string.IsNullOrWhiteSpace(profile.ApiKey))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", profile.ApiKey);
        }
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

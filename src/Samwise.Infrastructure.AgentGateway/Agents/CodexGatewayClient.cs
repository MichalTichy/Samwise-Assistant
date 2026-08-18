using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

namespace Samwise.Infrastructure.AgentGateway.Agents;

internal sealed class CodexGatewayClient : ICodexGatewayClient, IDisposable
{
    internal const string IntakeHttpClientName = "Samwise.Gateway.Intake";
    internal const string PlanningHttpClientName = "Samwise.Gateway.Planning";
    internal const string ExecutionHttpClientName = "Samwise.Gateway.Execution";

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

    public CodexGatewayClient(
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

    public async Task<GatewayStructuredResponse<TResponse>> RunStructuredAsync<TResponse>(
        GatewayStage stage,
        string prompt,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken = default)
    {
        var (profile, agent) = stage switch
        {
            GatewayStage.Intake => (intakeProfile, intakeAgent),
            GatewayStage.Planning => (planningProfile, planningAgent),
            GatewayStage.Execution => (executionProfile, executionAgent),
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
        };
        var options = CreateChatOptions<TResponse>(profile.ReasoningEffort, serializerOptions);
        var response = await agent.RunAsync<TResponse>(
            prompt,
            session: null,
            serializerOptions: serializerOptions,
            options: new ChatClientAgentRunOptions(options),
            cancellationToken: cancellationToken);

        return new GatewayStructuredResponse<TResponse>(
            response.Result,
            profile.Model,
            response.ResponseId,
            response.Text);
    }

    public async Task<GatewayToolCatalogSnapshot> GetToolsAsync(
        GatewayStage stage,
        CancellationToken cancellationToken = default)
    {
        var clientName = stage switch
        {
            GatewayStage.Planning => PlanningHttpClientName,
            GatewayStage.Execution => ExecutionHttpClientName,
            _ => throw new ArgumentOutOfRangeException(
                nameof(stage),
                stage,
                "Only planning and execution profiles expose tool catalogs.")
        };
        var client = httpClientFactory.CreateClient(clientName);
        using var response = await client.GetAsync("tools?detail=full", cancellationToken);
        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;
        var catalogVersion = root.GetProperty("catalog_version").GetString()
            ?? throw new InvalidOperationException("The Gateway tool catalog has no version.");

        var tools = new List<GatewayToolCapability>();
        foreach (var server in root.GetProperty("data").EnumerateArray())
        {
            foreach (var tool in server.GetProperty("tools").EnumerateArray())
            {
                tools.Add(new GatewayToolCapability(
                    tool.GetProperty("id").GetString() ?? throw new InvalidOperationException("A Gateway tool has no ID."),
                    tool.GetProperty("name").GetString() ?? string.Empty,
                    tool.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.String
                        ? description.GetString()
                        : null,
                    tool.GetProperty("input_schema").GetRawText(),
                    tool.GetProperty("can_invoke").GetBoolean()));
            }
        }

        return new GatewayToolCatalogSnapshot(catalogVersion, raw, tools);
    }

    public void Dispose()
    {
        intakeClient.Dispose();
        planningClient.Dispose();
        executionClient.Dispose();
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

    private static ChatOptions CreateChatOptions<TResponse>(
        string effort,
        JsonSerializerOptions serializerOptions)
    {
        var responseFormat = ChatResponseFormat.ForJsonSchema<TResponse>(serializerOptions);

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
}

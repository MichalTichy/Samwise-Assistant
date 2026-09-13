using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Samwise.Domain.Agents;
using Samwise.Infrastructure.AgentRuntimes.Agents;
using Samwise.Logic.Agents;

namespace Samwise.Infrastructure.CodexGateway.Catalogs;

internal sealed class CodexGatewayCatalogClient : IAgentToolCatalog, IAgentModelCatalog
{
    internal const string IntakeHttpClientName = "Samwise.CodexGatewayCatalog.Intake";
    internal const string PlanningHttpClientName = "Samwise.CodexGatewayCatalog.Planning";
    internal const string ExecutionHttpClientName = "Samwise.CodexGatewayCatalog.Execution";

    private readonly AgentEndpointOptions endpoints;
    private readonly IHttpClientFactory httpClientFactory;

    public CodexGatewayCatalogClient(
        IOptions<AgentEndpointOptions> options,
        IHttpClientFactory httpClientFactory)
    {
        endpoints = options.Value;
        this.httpClientFactory = httpClientFactory;
        ValidateProfile(endpoints.Intake, "Intake");
        ValidateProfile(endpoints.Planning, "Planning");
        ValidateProfile(endpoints.Execution, "Execution");
    }

    public async Task<IReadOnlyList<AgentModelDefinition>> GetModelsAsync(
        AgentStage stage,
        CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient(GetHttpClientName(stage));
        using var response = await client.GetAsync("models", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);

        return document.RootElement
            .GetProperty("data")
            .EnumerateArray()
            .Select(model => new AgentModelDefinition(
                model.GetProperty("id").GetString()
                    ?? throw new InvalidOperationException("A Gateway model has no ID."),
                model.GetProperty("supported_reasoning_efforts")
                    .EnumerateArray()
                    .Select(effort => effort.GetString()
                        ?? throw new InvalidOperationException("A Gateway reasoning effort is not a string."))
                    .ToList(),
                model.GetProperty("default_reasoning_effort").GetString()
                    ?? throw new InvalidOperationException("A Gateway model has no default reasoning effort.")))
            .ToList();
    }

    public async Task<ToolCatalogSnapshot> GetToolsAsync(
        AgentStage stage,
        CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient(GetHttpClientName(stage));
        using var response = await client.GetAsync("tools", cancellationToken);
        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;
        var catalogVersion = root.GetProperty("catalog_version").GetString()
            ?? throw new InvalidOperationException("The Gateway tool catalog has no version.");

        var servers = new List<ToolServerCapability>();
        foreach (var server in root.GetProperty("data").EnumerateArray())
        {
            var tools = new List<ToolCapability>();
            foreach (var tool in server.GetProperty("tools").EnumerateArray())
            {
                tools.Add(new ToolCapability(
                    tool.GetProperty("id").GetString() ?? throw new InvalidOperationException("A Gateway tool has no ID."),
                    tool.GetProperty("name").GetString() ?? string.Empty,
                    tool.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.String
                        ? description.GetString()
                        : null,
                    tool.GetProperty("input_schema").GetRawText(),
                    true));
            }

            servers.Add(new ToolServerCapability(
                server.GetProperty("id").GetString()
                    ?? throw new InvalidOperationException("A Gateway MCP server has no ID."),
                server.GetProperty("name").GetString() ?? string.Empty,
                server.GetProperty("version").GetString() ?? string.Empty,
                server.GetProperty("required").GetBoolean(),
                GetOptionalString(server, "title"),
                GetOptionalString(server, "description"),
                GetOptionalString(server, "website_url"),
                tools));
        }

        return new ToolCatalogSnapshot(catalogVersion, raw, servers);
    }

    private static string? GetOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string GetHttpClientName(AgentStage stage) => stage switch
    {
        AgentStage.Intake => IntakeHttpClientName,
        AgentStage.Planning => PlanningHttpClientName,
        AgentStage.Execution => ExecutionHttpClientName,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
    };

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

    internal static void ConfigureHttpClient(
        HttpClient client,
        AgentEndpointProfileOptions profile,
        TimeSpan timeout)
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

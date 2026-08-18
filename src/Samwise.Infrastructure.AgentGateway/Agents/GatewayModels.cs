using System.Text.Json;

namespace Samwise.Infrastructure.AgentGateway.Agents;

public enum GatewayStage
{
    Intake,
    Planning,
    Execution
}

public sealed record GatewayStructuredResponse<T>(
    T Result,
    string Model,
    string? CompletionId,
    string RawResponse);

public sealed record GatewayToolCatalogSnapshot(
    string Version,
    string RawJson,
    IReadOnlyList<GatewayToolCapability> Tools);

public sealed record GatewayToolCapability(
    string Id,
    string Name,
    string? Description,
    string InputSchemaJson,
    bool CanInvoke);

public interface ICodexGatewayClient
{
    Task<GatewayStructuredResponse<TResponse>> RunStructuredAsync<TResponse>(
        GatewayStage stage,
        string prompt,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken = default);

    Task<GatewayToolCatalogSnapshot> GetToolsAsync(
        GatewayStage stage,
        CancellationToken cancellationToken = default);
}

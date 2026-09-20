using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Samwise.Infrastructure.Teams.Authentication;
using Samwise.Infrastructure.Teams.Configuration;

namespace Samwise.Infrastructure.Teams.Graph;

internal sealed class TeamsGraphClient(
    HttpClient httpClient,
    TeamsGraphTokenProvider tokens,
    IOptions<TeamsMicrosoftGraphOptions> options)
{
    private readonly Uri baseUrl = options.Value.GraphBaseUrl;

    public Task<JsonElement> DelegatedAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(path, tokens.GetDelegatedTokenAsync, cancellationToken);

    public Task<JsonElement> ApplicationAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(path, tokens.GetApplicationTokenAsync, cancellationToken);

    public async Task<IReadOnlyList<JsonElement>> DelegatedCollectionAsync(
        string path,
        CancellationToken cancellationToken) =>
        ReadCollection(await DelegatedAsync(path, cancellationToken));

    public async Task<IReadOnlyList<JsonElement>> ApplicationCollectionAsync(
        string path,
        CancellationToken cancellationToken) =>
        ReadCollection(await ApplicationAsync(path, cancellationToken));

    public static string Id(string value) => Uri.EscapeDataString(
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A Microsoft Graph identifier is required.", nameof(value))
            : value.Trim());

    private async Task<JsonElement> SendAsync(
        string path,
        Func<CancellationToken, Task<string>> tokenFactory,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await tokenFactory(cancellationToken));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Microsoft Graph returned {(int)response.StatusCode}: {ReadError(text) ?? response.ReasonPhrase}");
        }

        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static IReadOnlyList<JsonElement> ReadCollection(JsonElement response)
    {
        if (!response.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Microsoft Graph returned no value collection.");
        }

        return value.EnumerateArray().Select(item => item.Clone()).ToList();
    }

    private static string? ReadError(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String
                    ? message.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

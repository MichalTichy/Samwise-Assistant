using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Samwise.Infrastructure.Todoist.Api;

internal sealed class TodoistApiClient(HttpClient httpClient)
{
    public const string BaseAddress = "https://api.todoist.com/api/v1/";
    private const int PageLimit = 200;
    private const int MaximumPages = 50;
    private const int MaximumRateLimitRetries = 2;
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(30);

    public async Task<JsonElement> GetAsync(string path, string token, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaximumRateLimitRetries)
            {
                await Task.Delay(GetRetryDelay(response), cancellationToken);
                continue;
            }

            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Todoist returned {(int)response.StatusCode}: {ReadError(text) ?? response.ReasonPhrase}");
            }

            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
    }

    /// <summary>Reads a v1 list endpoint, following <c>next_cursor</c> until every page is loaded.</summary>
    public async Task<IReadOnlyList<JsonElement>> GetCollectionAsync(
        string path,
        string token,
        CancellationToken cancellationToken)
    {
        var items = new List<JsonElement>();
        string? cursor = null;
        for (var page = 0; page < MaximumPages; page++)
        {
            var separator = path.Contains('?', StringComparison.Ordinal) ? '&' : '?';
            var pagePath = $"{path}{separator}limit={PageLimit}"
                + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");
            var response = await GetAsync(pagePath, token, cancellationToken);
            if (response.ValueKind == JsonValueKind.Array)
            {
                items.AddRange(response.EnumerateArray());
                return items;
            }

            if (!response.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("Todoist returned no results collection.");
            }

            items.AddRange(results.EnumerateArray());
            cursor = response.TryGetProperty("next_cursor", out var next) && next.ValueKind == JsonValueKind.String
                ? next.GetString()
                : null;
            if (string.IsNullOrEmpty(cursor))
            {
                return items;
            }
        }

        throw new InvalidOperationException($"Todoist returned more than {MaximumPages} pages for {path}.");
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response)
    {
        var delay = response.Headers.RetryAfter?.Delta
            ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null)
            ?? TimeSpan.FromSeconds(5);
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay > MaximumRetryDelay ? MaximumRetryDelay : delay;
    }

    private static string? ReadError(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String
                    ? error.GetString()
                    : text.Trim();
        }
        catch (JsonException)
        {
            return text.Trim();
        }
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Samwise.Infrastructure.Mcp.Serialization;

internal static class SamwiseMcpJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static string SerializeRuntime(object value) =>
        JsonSerializer.Serialize(value, value.GetType(), Options);

    public static string Truncate(string value, int maximumLength)
    {
        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : string.Concat(normalized.AsSpan(0, maximumLength - 1), "…");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

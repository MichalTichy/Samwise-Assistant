using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace Samwise.Web.Components.Shared;

public partial class ToolCallParameters
{
    private JsonElement? root;
    private string? parseError;

    [Parameter, EditorRequired]
    public string ArgumentsJson { get; set; } = string.Empty;

    protected override void OnParametersSet()
    {
        try
        {
            using var document = JsonDocument.Parse(ArgumentsJson);
            root = document.RootElement.Clone();
            parseError = null;
        }
        catch (JsonException exception)
        {
            root = null;
            parseError = exception.Message;
        }
    }
}

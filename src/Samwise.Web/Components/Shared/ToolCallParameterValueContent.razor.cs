using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace Samwise.Web.Components.Shared;

public partial class ToolCallParameterValueContent
{
    [Parameter, EditorRequired]
    public JsonElement Value { get; set; }

    private static IReadOnlyList<string> Paragraphs(string text) => Regex
        .Split(text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim(), "\\n\\s*\\n")
        .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph))
        .Select(paragraph => paragraph.Trim())
        .ToList();
}

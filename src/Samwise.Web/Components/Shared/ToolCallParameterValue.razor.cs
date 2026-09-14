using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace Samwise.Web.Components.Shared;

public partial class ToolCallParameterValue
{
    private static readonly IReadOnlyDictionary<string, string> KnownLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["accountId"] = "Account ID",
            ["attachmentId"] = "Attachment ID",
            ["bodyFormat"] = "Body format",
            ["calendarId"] = "Calendar ID",
            ["dueString"] = "Due date",
            ["emailId"] = "Email ID",
            ["eventId"] = "Event ID",
            ["projectId"] = "Project ID",
            ["replyAll"] = "Reply all",
            ["timeZone"] = "Time zone",
            ["workItemId"] = "Work Item ID"
        };

    [Parameter]
    public string? Name { get; set; }

    [Parameter, EditorRequired]
    public JsonElement Value { get; set; }

    private static string Humanize(string name)
    {
        if (KnownLabels.TryGetValue(name, out var knownLabel))
        {
            return knownLabel;
        }

        var normalized = name.Replace('_', ' ').Replace('-', ' ');
        normalized = Regex.Replace(normalized, "(?<=[a-z0-9])(?=[A-Z])", " ");
        if (normalized.Length == 0)
        {
            return name;
        }

        var result = new StringBuilder(normalized.ToLowerInvariant());
        result[0] = char.ToUpperInvariant(result[0]);
        return result.ToString();
    }
}

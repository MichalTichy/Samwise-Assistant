using System.Text.Json;
using LumexUI.Common;
using Microsoft.AspNetCore.Components;
using Samwise.Domain.Agents;

namespace Samwise.Web.Components.Shared;

public partial class AgentInvocationDetails
{
    [Parameter]
    public AgentInvocation? Invocation { get; set; }

    [Parameter]
    public bool Open { get; set; }

    [Parameter]
    public EventCallback<bool> OpenChanged { get; set; }

    private Task HandleOpenChangedAsync(bool open) => OpenChanged.InvokeAsync(open);

    private Task CloseAsync() => OpenChanged.InvokeAsync(false);

    private static ThemeColor StatusColor(AgentInvocationStatus status) => status switch
    {
        AgentInvocationStatus.Succeeded => ThemeColor.Success,
        AgentInvocationStatus.Failed or AgentInvocationStatus.OutcomeUnknown => ThemeColor.Danger,
        _ => ThemeColor.Info
    };

    private static string FormatStatus(AgentInvocationStatus status) => status switch
    {
        AgentInvocationStatus.OutcomeUnknown => "Outcome unknown",
        _ => status.ToString()
    };

    private static string FormatFinishedAt(AgentInvocation invocation) =>
        invocation.FinishedAt?.ToLocalTime().ToString("G") ?? "Still running";

    private static string FormatDuration(AgentInvocation invocation)
    {
        var finishedAt = invocation.FinishedAt ?? DateTimeOffset.UtcNow;
        var duration = finishedAt - invocation.StartedAt;
        if (duration < TimeSpan.Zero)
        {
            return "Unavailable";
        }

        if (duration.TotalSeconds < 1)
        {
            return "Less than a second";
        }

        return duration.TotalMinutes < 1
            ? $"{duration.TotalSeconds:0.#} seconds"
            : duration.TotalHours < 1
                ? $"{(int)duration.TotalMinutes}m {duration.Seconds}s"
                : $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";
    }

    private static string ValueOrUnavailable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Not recorded" : value;

    private static string FormatJson(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return value;
        }
    }
}

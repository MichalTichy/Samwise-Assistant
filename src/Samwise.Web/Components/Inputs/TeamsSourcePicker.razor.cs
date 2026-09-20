using Microsoft.AspNetCore.Components;
using Samwise.Inputs.Teams.Configuration;
using Samwise.Inputs.Teams.Discovery;

namespace Samwise.Web.Components.Inputs;

public partial class TeamsSourcePicker
{
    private string query = string.Empty;
    private bool showSuggestions;

    [Parameter, EditorRequired]
    public IReadOnlyList<TeamsInputSourceOption> Options { get; set; } = [];

    [Parameter, EditorRequired]
    public IReadOnlyList<TeamsInputSource> Selected { get; set; } = [];

    [Parameter]
    public EventCallback<IReadOnlyList<TeamsInputSource>> SelectedChanged { get; set; }

    [Parameter]
    public string Placeholder { get; set; } = "Search…";

    [Parameter]
    public string EmptyText { get; set; } = "Nothing is ignored.";

    private IReadOnlyList<TeamsInputSourceOption> FilteredOptions => Options
        .Where(option => Selected.All(source => !string.Equals(source.Id, option.Id, StringComparison.Ordinal)))
        .Where(option => string.IsNullOrWhiteSpace(query)
            || option.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || (option.Detail?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
        .Take(8)
        .ToList();

    private void QueryChanged(ChangeEventArgs args)
    {
        query = args.Value?.ToString() ?? string.Empty;
        showSuggestions = true;
    }

    private void ShowSuggestions() => showSuggestions = true;

    private async Task AddAsync(TeamsInputSourceOption option)
    {
        var updated = Selected
            .Append(new TeamsInputSource { Id = option.Id, DisplayName = option.DisplayName })
            .OrderBy(source => source.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        query = string.Empty;
        showSuggestions = false;
        await SelectedChanged.InvokeAsync(updated);
    }

    private async Task RemoveAsync(string id)
    {
        var updated = Selected
            .Where(source => !string.Equals(source.Id, id, StringComparison.Ordinal))
            .ToList();
        await SelectedChanged.InvokeAsync(updated);
    }
}

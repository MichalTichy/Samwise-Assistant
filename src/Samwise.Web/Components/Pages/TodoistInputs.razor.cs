using MediatR;
using Microsoft.AspNetCore.Components;
using Samwise.Inputs.Todoist.Configuration;
using Samwise.Inputs.Todoist.Discovery;
using Samwise.Inputs.Todoist.Ingestion;
using Samwise.Inputs.Todoist.Usecases.Configuration;
using Samwise.Inputs.Todoist.Tasks;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Web.Components.Pages;

public partial class TodoistInputs
{
    private string apiToken = string.Empty;
    private bool hasApiToken;
    private bool removeApiToken;
    private int pollingIntervalMinutes = TodoistInputConfiguration.DefaultPollingIntervalMinutes;
    private int leadHours = TodoistInputConfiguration.DefaultLeadHours;
    private int overdueDays = TodoistInputConfiguration.DefaultOverdueDays;
    private string intakeGuidance = string.Empty;
    private List<TodoistInputSource> ignoredProjects = [];
    private List<TodoistInputSource> ignoredLabels = [];
    private List<TodoistInputSource> includedProjects = [];
    private List<TodoistInputSource> includedLabels = [];
    private TodoistInputCatalog? catalog;
    private DateTimeOffset? lastSuccessfulCheckAt;
    private string? lastError;
    private bool loading = true;
    private bool loadingCatalog;
    private bool saving;
    private bool processing;
    private string? pageMessage;
    private bool pageMessageIsSuccess;

    [Inject]
    private ISender Sender { get; set; } = default!;

    [Inject]
    private IReadOnlyRepository<TodoistInputConfiguration> ConfigurationRepository { get; set; } = default!;

    [Inject]
    private ITodoistTaskReader TaskReader { get; set; } = default!;

    // Saved selections stay visible even when the catalogue is unavailable or no longer contains them.
    private IReadOnlyList<TodoistInputSource> ProjectOptions =>
        MergeOptions(catalog?.Projects ?? [], includedProjects.Concat(ignoredProjects));

    private IReadOnlyList<TodoistInputSource> LabelOptions =>
        MergeOptions(catalog?.Labels ?? [], includedLabels.Concat(ignoredLabels));

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        await LoadAsync();
        StateHasChanged();
        if (hasApiToken)
        {
            await LoadCatalogAsync();
        }
    }

    private async Task LoadAsync()
    {
        loading = true;
        pageMessage = null;
        pageMessageIsSuccess = false;
        try
        {
            var configuration = await ConfigurationRepository.GetRequiredByIdAsync(
                TodoistInputConfiguration.DocumentId);
            apiToken = string.Empty;
            hasApiToken = configuration.IsConfigured;
            removeApiToken = false;
            pollingIntervalMinutes = configuration.PollingIntervalMinutes;
            leadHours = configuration.LeadHours;
            overdueDays = configuration.OverdueDays;
            intakeGuidance = configuration.IntakeGuidance;
            ignoredProjects = configuration.IgnoredProjects.ToList();
            ignoredLabels = configuration.IgnoredLabels.ToList();
            includedProjects = configuration.IncludedProjects.ToList();
            includedLabels = configuration.IncludedLabels.ToList();
            lastSuccessfulCheckAt = configuration.LastSuccessfulCheckAt;
            lastError = configuration.LastError;
            loadingCatalog = hasApiToken;
        }
        catch (Exception exception)
        {
            pageMessage = $"The Todoist settings could not be loaded: {exception.Message}";
        }
        finally
        {
            loading = false;
        }
    }

    private async Task LoadCatalogAsync()
    {
        if (!hasApiToken || loading)
        {
            return;
        }

        loadingCatalog = true;
        pageMessage = null;
        pageMessageIsSuccess = false;
        StateHasChanged();
        try
        {
            catalog = await TaskReader.GetCatalogAsync();
        }
        catch (Exception exception)
        {
            catalog = null;
            pageMessage = $"Todoist could not be reached: {exception.Message}";
        }
        finally
        {
            loadingCatalog = false;
            StateHasChanged();
        }
    }

    private async Task SaveAsync()
    {
        saving = true;
        pageMessage = null;
        pageMessageIsSuccess = false;
        try
        {
            var hadEnteredApiToken = !string.IsNullOrWhiteSpace(apiToken);
            var result = await Sender.Send(new SaveTodoistInputConfigurationUsecase(
                apiToken,
                removeApiToken,
                pollingIntervalMinutes,
                leadHours,
                overdueDays,
                intakeGuidance,
                ignoredProjects,
                ignoredLabels, includedProjects, includedLabels));
            pageMessage = result.Succeeded ? "Todoist task source saved." : string.Join(" ", result.Errors);
            pageMessageIsSuccess = result.Succeeded;
            if (!result.Succeeded)
            {
                return;
            }

            hasApiToken = !removeApiToken && (hasApiToken || hadEnteredApiToken);
            apiToken = string.Empty;
            removeApiToken = false;
            if (!hasApiToken)
            {
                catalog = null;
            }
            else if (hadEnteredApiToken)
            {
                await LoadCatalogAsync();
                if (catalog is not null)
                {
                    pageMessage = "Todoist task source saved and the connection works.";
                    pageMessageIsSuccess = true;
                }
            }
        }
        finally
        {
            saving = false;
        }
    }

    private async Task ProcessNowAsync()
    {
        processing = true;
        pageMessage = null;
        pageMessageIsSuccess = false;
        try
        {
            var result = await Sender.Send(new SyncTodoistTasksUsecase());
            pageMessage = $"Todoist: {result.Imported} imported, {result.Ignored} ignored, "
                + $"{result.AlreadyProcessed} synchronized."
                + (result.Errors.Count == 0 ? string.Empty : $" {string.Join(" ", result.Errors)}");
            pageMessageIsSuccess = result.Errors.Count == 0;
            var configuration = await ConfigurationRepository.GetRequiredByIdAsync(
                TodoistInputConfiguration.DocumentId);
            lastSuccessfulCheckAt = configuration.LastSuccessfulCheckAt;
            lastError = configuration.LastError;
        }
        finally
        {
            processing = false;
        }
    }

    private static bool IsSelected(IEnumerable<TodoistInputSource> selected, string id) =>
        selected.Any(source => string.Equals(source.Id, id, StringComparison.Ordinal));

    private static void Toggle(List<TodoistInputSource> selected, TodoistInputSource option)
    {
        if (selected.RemoveAll(source => string.Equals(source.Id, option.Id, StringComparison.Ordinal)) == 0)
        {
            selected.Add(new TodoistInputSource { Id = option.Id, DisplayName = option.DisplayName });
        }
    }

    private static IReadOnlyList<TodoistInputSource> MergeOptions(
        IEnumerable<TodoistInputSourceOption> options,
        IEnumerable<TodoistInputSource> selected) => options
        .Select(option => new TodoistInputSource { Id = option.Id, DisplayName = option.DisplayName })
        .Concat(selected)
        .GroupBy(source => source.Id, StringComparer.Ordinal)
        .Select(group => group.First())
        .OrderBy(source => source.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static string FormatLastCheck(DateTimeOffset? value) => value is null
        ? "No Todoist tasks checked yet"
        : $"Last checked {value.Value.ToLocalTime():g}";
}

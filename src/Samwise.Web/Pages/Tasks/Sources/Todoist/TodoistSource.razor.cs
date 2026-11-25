using Samwise.Web.Components;
using Microsoft.AspNetCore.Components;
using Samwise.TaskSources.Todoist.Configuration;
using Samwise.TaskSources.Todoist.Discovery;
using Samwise.TaskSources.Todoist.Synchronization;
using Samwise.TaskSources.Todoist.Usecases.Configuration;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Web.Pages.Tasks.Sources.Todoist;

public partial class TodoistSource : AppComponentBase
{
    private string apiToken = string.Empty;
    private bool hasApiToken;
    private bool removeApiToken;
    private int pollingIntervalMinutes = TodoistTaskSourceConfiguration.DefaultPollingIntervalMinutes;
    private int leadHours = TodoistTaskSourceConfiguration.DefaultLeadHours;
    private int overdueDays = TodoistTaskSourceConfiguration.DefaultOverdueDays;
    private string intakeGuidance = string.Empty;
    private List<TodoistSourceSelection> ignoredProjects = [];
    private List<TodoistSourceSelection> ignoredLabels = [];
    private TodoistSourceCatalog? catalog;
    private DateTimeOffset? lastSuccessfulCheckAt;
    private string? lastError;
    private bool loadingCatalog;
    private bool saving;
    private bool processing;
    private string? pageMessage;
    private bool pageMessageIsSuccess;

    [Inject]
    private IReadOnlyRepository<TodoistTaskSourceConfiguration> ConfigurationRepository { get; set; } = default!;

    [Inject]
    private ITodoistTaskReader TaskReader { get; set; } = default!;

    // Saved selections stay visible even when the catalogue is unavailable or no longer contains them.
    private IReadOnlyList<TodoistSourceSelection> ProjectOptions =>
        MergeOptions(catalog?.Projects ?? [], ignoredProjects);

    private IReadOnlyList<TodoistSourceSelection> LabelOptions =>
        MergeOptions(catalog?.Labels ?? [], ignoredLabels);

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

    private Task LoadAsync()
    {
        pageMessage = null;
        pageMessageIsSuccess = false;
        return ExecuteLoadAsync(token => ConfigurationRepository.GetRequiredByIdAsync(TodoistTaskSourceConfiguration.DocumentId, token), configuration =>
        {
            apiToken = string.Empty;
            hasApiToken = configuration.IsConfigured;
            removeApiToken = false;
            pollingIntervalMinutes = configuration.PollingIntervalMinutes;
            leadHours = configuration.LeadHours;
            overdueDays = configuration.OverdueDays;
            intakeGuidance = configuration.IntakeGuidance;
            ignoredProjects = configuration.IgnoredProjects.ToList();
            ignoredLabels = configuration.IgnoredLabels.ToList();
            lastSuccessfulCheckAt = configuration.LastSuccessfulCheckAt;
            lastError = configuration.LastError;
            loadingCatalog = hasApiToken;
        });
    }

    private Task LoadCatalogAsync() => ExecuteActionAsync(async () =>
    {
        if (!hasApiToken || IsLoading)
        {
            return;
        }

        loadingCatalog = true;
        pageMessage = null;
        pageMessageIsSuccess = false;
        StateHasChanged();
        try
        {
            catalog = await TaskReader.GetCatalogAsync(ComponentToken);
        }
        catch (Exception exception) when (!ComponentToken.IsCancellationRequested)
        {
            catalog = null;
            pageMessage = $"Todoist could not be reached: {exception.Message}";
        }
        finally
        {
            loadingCatalog = false;
            StateHasChanged();
        }
    });

    private Task SaveAsync() => ExecuteActionAsync(async () =>
    {
        saving = true;
        pageMessage = null;
        pageMessageIsSuccess = false;
        try
        {
            var hadEnteredApiToken = !string.IsNullOrWhiteSpace(apiToken);
            var result = await SendAsync(new SaveTodoistTaskSourceConfigurationUsecase(
                apiToken,
                removeApiToken,
                pollingIntervalMinutes,
                leadHours,
                overdueDays,
                intakeGuidance,
                ignoredProjects,
                ignoredLabels));
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
    });

    private Task ProcessNowAsync() => ExecuteActionAsync(async () =>
    {
        processing = true;
        pageMessage = null;
        pageMessageIsSuccess = false;
        try
        {
            var result = await SendAsync(new SyncTodoistTasksUsecase());
            pageMessage = $"Todoist: {result.Imported} imported, {result.Ignored} ignored, "
                + $"{result.AlreadyProcessed} synchronized."
                + (result.Errors.Count == 0 ? string.Empty : $" {string.Join(" ", result.Errors)}");
            pageMessageIsSuccess = result.Errors.Count == 0;
            var configuration = await ConfigurationRepository.GetRequiredByIdAsync(
                TodoistTaskSourceConfiguration.DocumentId);
            lastSuccessfulCheckAt = configuration.LastSuccessfulCheckAt;
            lastError = configuration.LastError;
        }
        finally
        {
            processing = false;
        }
    });

    private static bool IsSelected(IEnumerable<TodoistSourceSelection> selected, string id) =>
        selected.Any(source => string.Equals(source.Id, id, StringComparison.Ordinal));

    private static void Toggle(List<TodoistSourceSelection> selected, TodoistSourceSelection option)
    {
        if (selected.RemoveAll(source => string.Equals(source.Id, option.Id, StringComparison.Ordinal)) == 0)
        {
            selected.Add(new TodoistSourceSelection { Id = option.Id, DisplayName = option.DisplayName });
        }
    }

    private static IReadOnlyList<TodoistSourceSelection> MergeOptions(
        IEnumerable<TodoistSourceOption> options,
        IEnumerable<TodoistSourceSelection> selected) => options
        .Select(option => new TodoistSourceSelection { Id = option.Id, DisplayName = option.DisplayName })
        .Concat(selected)
        .GroupBy(source => source.Id, StringComparer.Ordinal)
        .Select(group => group.First())
        .OrderBy(source => source.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static string FormatLastCheck(DateTimeOffset? value) => value is null
        ? "No Todoist tasks checked yet"
        : $"Last checked {value.Value.ToLocalTime():g}";
}

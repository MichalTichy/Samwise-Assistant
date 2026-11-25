using Samwise.Web.Components;
using Microsoft.AspNetCore.Components;
using Samwise.Inputs.Todoist.Configuration;
using Samwise.Inputs.Todoist.Discovery;
using Samwise.Inputs.Todoist.Ingestion;
using Samwise.Inputs.Todoist.Usecases.Configuration;
using Samwise.Inputs.Todoist.Usecases.Processing;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Web.Pages.Inputs;

public partial class TodoistInputs : AppComponentBase
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
    private TodoistInputCatalog? catalog;
    private DateTimeOffset? lastSuccessfulCheckAt;
    private string? lastError;
    private bool loadingCatalog;
    private bool saving;
    private bool processing;
    private string? pageMessage;
    private bool pageMessageIsSuccess;

    [Inject]
    private IReadOnlyRepository<TodoistInputConfiguration> ConfigurationRepository { get; set; } = default!;

    [Inject]
    private ITodoistTaskReader TaskReader { get; set; } = default!;

    // Saved selections stay visible even when the catalogue is unavailable or no longer contains them.
    private IReadOnlyList<TodoistInputSource> ProjectOptions =>
        MergeOptions(catalog?.Projects ?? [], ignoredProjects);

    private IReadOnlyList<TodoistInputSource> LabelOptions =>
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
        return ExecuteLoadAsync(token => ConfigurationRepository.GetRequiredByIdAsync(TodoistInputConfiguration.DocumentId, token), configuration =>
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
            var result = await SendAsync(new SaveTodoistInputConfigurationUsecase(
                apiToken,
                removeApiToken,
                pollingIntervalMinutes,
                leadHours,
                overdueDays,
                intakeGuidance,
                ignoredProjects,
                ignoredLabels));
            pageMessage = result.Succeeded ? "Todoist input settings saved." : string.Join(" ", result.Errors);
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
                    pageMessage = "Todoist input settings saved and the connection works.";
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
            var result = await SendAsync(new ProcessTodoistInputsUsecase());
            pageMessage = $"Todoist: {result.Imported} imported, {result.Ignored} ignored, "
                + $"{result.AlreadyProcessed} already processed."
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
    });

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

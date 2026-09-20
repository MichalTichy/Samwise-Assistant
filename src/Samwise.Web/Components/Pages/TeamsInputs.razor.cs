using MediatR;
using Microsoft.AspNetCore.Components;
using Samwise.Inputs.Teams;
using Samwise.Inputs.Teams.Usecases;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Web.Components.Pages;

public partial class TeamsInputs
{
    private int pollingIntervalMinutes = TeamsInputConfiguration.DefaultPollingIntervalMinutes;
    private int debounceMinutes = TeamsInputConfiguration.DefaultDebounceMinutes;
    private string intakeGuidance = string.Empty;
    private bool includeChatMessages = true;
    private bool includeChannelMessages = true;
    private bool includeCallTranscripts;
    private List<TeamsInputSource> ignoredPeople = [];
    private List<TeamsInputSource> ignoredChats = [];
    private List<TeamsInputSource> ignoredChannels = [];
    private TeamsInputCatalog catalog = new([], [], []);
    private DateTimeOffset? lastSuccessfulCheckAt;
    private bool loading;
    private bool loadingCatalog;
    private bool saving;
    private bool processing;
    private string? pageMessage;
    private bool pageMessageIsSuccess;

    [Inject]
    private ISender Sender { get; set; } = default!;

    [Inject]
    private IReadOnlyRepository<TeamsInputConfiguration> ConfigurationRepository { get; set; } = default!;

    [Inject]
    private ITeamsInputReader InputReader { get; set; } = default!;

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        loading = true;
        loadingCatalog = true;
        pageMessage = null;
        pageMessageIsSuccess = false;
        try
        {
            var configurationTask = ConfigurationRepository.GetRequiredByIdAsync(
                TeamsInputConfiguration.DocumentId);
            var catalogTask = InputReader.GetCatalogAsync();
            var configuration = await configurationTask;
            pollingIntervalMinutes = configuration.PollingIntervalMinutes;
            debounceMinutes = configuration.DebounceMinutes;
            intakeGuidance = configuration.IntakeGuidance;
            includeChatMessages = configuration.IncludeChatMessages;
            includeChannelMessages = configuration.IncludeChannelMessages;
            includeCallTranscripts = configuration.IncludeCallTranscripts;
            ignoredPeople = configuration.IgnoredPeople.ToList();
            ignoredChats = configuration.IgnoredChats.ToList();
            ignoredChannels = configuration.IgnoredChannels.ToList();
            lastSuccessfulCheckAt = configuration.LastSuccessfulCheckAt;
            catalog = await catalogTask;
        }
        catch (Exception exception)
        {
            pageMessage = $"The Teams catalogue could not be loaded: {exception.Message}";
        }
        finally
        {
            loadingCatalog = false;
            loading = false;
        }
    }

    private async Task SaveAsync()
    {
        saving = true;
        pageMessage = null;
        pageMessageIsSuccess = false;
        try
        {
            var result = await Sender.Send(new SaveTeamsInputConfigurationUsecase(
                pollingIntervalMinutes,
                debounceMinutes,
                intakeGuidance,
                includeChatMessages,
                includeChannelMessages,
                includeCallTranscripts,
                ignoredPeople,
                ignoredChats,
                ignoredChannels));
            pageMessage = result.Succeeded ? "Teams input settings saved." : string.Join(" ", result.Errors);
            pageMessageIsSuccess = result.Succeeded;
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
            var result = await Sender.Send(new ProcessTeamsInputsUsecase());
            pageMessage = $"Teams: {result.Imported} imported, {result.Ignored} ignored, "
                + $"{result.AlreadyProcessed} already processed."
                + (result.Errors.Count == 0 ? string.Empty : $" {string.Join(" ", result.Errors)}");
            pageMessageIsSuccess = result.Errors.Count == 0;
            var configuration = await ConfigurationRepository.GetRequiredByIdAsync(
                TeamsInputConfiguration.DocumentId);
            lastSuccessfulCheckAt = configuration.LastSuccessfulCheckAt;
        }
        finally
        {
            processing = false;
        }
    }

    private Task SetIgnoredPeople(IReadOnlyList<TeamsInputSource> value)
    {
        ignoredPeople = value.ToList();
        return Task.CompletedTask;
    }

    private Task SetIgnoredChats(IReadOnlyList<TeamsInputSource> value)
    {
        ignoredChats = value.ToList();
        return Task.CompletedTask;
    }

    private Task SetIgnoredChannels(IReadOnlyList<TeamsInputSource> value)
    {
        ignoredChannels = value.ToList();
        return Task.CompletedTask;
    }

    private static string FormatLastCheck(DateTimeOffset? value) => value is null
        ? "No Teams activity checked yet"
        : $"Last checked {value.Value.ToLocalTime():g}";
}

using Samwise.Web.Components;
using MediatR;
using Microsoft.AspNetCore.Components;
using Samwise.Inputs.Teams.Authentication;
using Samwise.Inputs.Teams.Configuration;
using Samwise.Inputs.Teams.Discovery;
using Samwise.Inputs.Teams.Ingestion;
using Samwise.Inputs.Teams.Usecases.Authorization;
using Samwise.Inputs.Teams.Usecases.Configuration;
using Samwise.Inputs.Teams.Usecases.Processing;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Web.Pages.Inputs;

public partial class TeamsInputs : AppComponentBase
{
    private string tenantId = "organizations";
    private string clientId = string.Empty;
    private string clientSecret = string.Empty;
    private bool hasClientSecret;
    private bool removeClientSecret;
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
    private bool loadingCatalog;
    private bool refreshingConnection;
    private bool saving;
    private bool processing;
    private bool startingAuthorization;
    private bool disconnecting;
    private TeamsConnectionState connectionState = new(false, null, null, null);
    private string? pageMessage;
    private bool pageMessageIsSuccess;

    [Inject]
    private ISender Sender { get; set; } = default!;

    [Inject]
    private IReadOnlyRepository<TeamsInputConfiguration> ConfigurationRepository { get; set; } = default!;

    [Inject]
    private ITeamsInputReader InputReader { get; set; } = default!;

    [Inject]
    private ITeamsAuthorizationService AuthorizationService { get; set; } = default!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        await LoadAsync();
        StateHasChanged();
        if (connectionState.Connected)
        {
            await LoadCatalogAsync();
        }
    }

    private Task LoadAsync()
    {
        pageMessage = null;
        pageMessageIsSuccess = false;
        return ExecuteLoadAsync(async token =>
        {
            var configuration = await ConfigurationRepository.GetRequiredByIdAsync(
                TeamsInputConfiguration.DocumentId, token);
            var connection = await AuthorizationService.GetStateAsync(token);
            return (configuration, connection);
        }, result =>
        {
            var configuration = result.configuration;
            tenantId = configuration.MicrosoftGraph.TenantId;
            clientId = configuration.MicrosoftGraph.ClientId;
            clientSecret = string.Empty;
            hasClientSecret = configuration.MicrosoftGraph.HasClientSecret;
            removeClientSecret = false;
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
            connectionState = result.connection;
            loadingCatalog = connectionState.Connected;
            if (!connectionState.Connected)
            {
                catalog = new TeamsInputCatalog([], [], []);
            }
        });
    }

    private async Task LoadCatalogAsync()
    {
        if (!connectionState.Connected || IsLoading)
        {
            return;
        }

        loadingCatalog = true;
        pageMessage = null;
        pageMessageIsSuccess = false;
        StateHasChanged();
        try
        {
            catalog = await InputReader.GetCatalogAsync(ComponentToken);
        }
        catch (Exception exception)
        {
            pageMessage = $"The Teams catalogue could not be loaded: {exception.Message}";
        }
        finally
        {
            loadingCatalog = false;
            StateHasChanged();
        }
    }

    private async Task RefreshConnectionAsync()
    {
        refreshingConnection = true;
        try
        {
            connectionState = await AuthorizationService.GetStateAsync(ComponentToken);
            if (connectionState.Connected)
            {
                await LoadCatalogAsync();
            }
            else
            {
                catalog = new TeamsInputCatalog([], [], []);
            }
        }
        finally
        {
            refreshingConnection = false;
        }
    }

    private async Task StartAuthorizationAsync()
    {
        startingAuthorization = true;
        pageMessage = null;
        pageMessageIsSuccess = false;
        try
        {
            var hadEnteredClientSecret = !string.IsNullOrWhiteSpace(clientSecret);
            var saveResult = await Sender.Send(CreateSaveRequest());
            if (!saveResult.Succeeded)
            {
                pageMessage = string.Join(" ", saveResult.Errors);
                return;
            }

            clientSecret = string.Empty;
            hasClientSecret = removeClientSecret ? false : hasClientSecret || hadEnteredClientSecret;
            removeClientSecret = false;
            var result = await Sender.Send(new StartTeamsAuthorizationUsecase());
            if (result.Succeeded)
            {
                connectionState = connectionState with { PendingChallenge = result.Challenge, Error = null };
            }
            else
            {
                pageMessage = string.Join(" ", result.Errors);
            }
        }
        finally
        {
            startingAuthorization = false;
        }
    }

    private async Task DisconnectAsync()
    {
        disconnecting = true;
        pageMessage = null;
        pageMessageIsSuccess = false;
        try
        {
            await Sender.Send(new DisconnectTeamsAuthorizationUsecase());
            connectionState = new TeamsConnectionState(false, null, null, null);
            catalog = new TeamsInputCatalog([], [], []);
            pageMessage = "Microsoft Teams account disconnected.";
            pageMessageIsSuccess = true;
        }
        finally
        {
            disconnecting = false;
        }
    }

    private async Task SaveAsync()
    {
        saving = true;
        pageMessage = null;
        pageMessageIsSuccess = false;
        try
        {
            var hadEnteredClientSecret = !string.IsNullOrWhiteSpace(clientSecret);
            var result = await Sender.Send(CreateSaveRequest());
            pageMessage = result.Succeeded ? "Teams input settings saved." : string.Join(" ", result.Errors);
            pageMessageIsSuccess = result.Succeeded;
            if (result.Succeeded)
            {
                hasClientSecret = removeClientSecret ? false : hasClientSecret || hadEnteredClientSecret;
                clientSecret = string.Empty;
                removeClientSecret = false;
                connectionState = await AuthorizationService.GetStateAsync(ComponentToken);
            }
        }
        finally
        {
            saving = false;
        }
    }

    private SaveTeamsInputConfigurationUsecase CreateSaveRequest() => new(
        tenantId,
        clientId,
        clientSecret,
        removeClientSecret,
        pollingIntervalMinutes,
        debounceMinutes,
        intakeGuidance,
        includeChatMessages,
        includeChannelMessages,
        includeCallTranscripts,
        ignoredPeople,
        ignoredChats,
        ignoredChannels);

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
                + (result.Errors.Count == 0 ? string.Empty : $" {string.Join(" ", result.Errors)}")
                + (result.SkippedMeetings.Count == 0
                    ? string.Empty
                    : $" {string.Join(" ", result.SkippedMeetings)}");
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

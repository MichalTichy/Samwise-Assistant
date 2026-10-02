using MediatR;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Samwise.Domain.Notifications;
using Samwise.Logic.Notifications.Services;
using Samwise.Logic.Notifications.Specifications;
using Samwise.Logic.Notifications.Usecases;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Web.Components.Pages;

public partial class NotificationSettings : IAsyncDisposable
{
    private IJSObjectReference? pushModule;
    private DotNetObjectReference<NotificationSettings>? dotNetReference;
    private ElementReference enableButton;
    private ElementReference? boundButton;
    private string? publicKey;
    private BrowserPushState? browserState;
    private IReadOnlyList<PushDevice> devices = [];
    private bool notifyPlanAwaitingApproval = true;
    private bool notifyQuestionAsked = true;
    private bool notifyNeedsAttention = true;
    private bool notifyCompleted;
    private bool notifyDailyAutoExecutionSummary = true;
    private bool loading = true;
    private bool busy;
    private bool sendingTest;
    private string? pageMessage;
    private bool pageMessageIsSuccess;

    [Inject]
    private ISender Sender { get; set; } = default!;

    [Inject]
    private IReadOnlyRepository<Samwise.Domain.Notifications.NotificationSettings> SettingsRepository { get; set; } = default!;

    [Inject]
    private IReadOnlyRepository<PushDevice> DeviceRepository { get; set; } = default!;

    [Inject]
    private IPushApplicationKeyProvider KeyProvider { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    private PushDevice? ThisDevice => browserState?.Endpoint is { } endpoint
        ? devices.FirstOrDefault(device => device.Id == PushDevice.CreateId(endpoint))
        : null;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await LoadAsync();
            StateHasChanged();
            try
            {
                publicKey = await KeyProvider.GetPublicKeyAsync(CancellationToken.None);
                pushModule = await JS.InvokeAsync<IJSObjectReference>("import", "./js/push.js");
                dotNetReference = DotNetObjectReference.Create(this);
                browserState = await pushModule.InvokeAsync<BrowserPushState>("getState");
            }
            catch (Exception exception)
            {
                browserState = new BrowserPushState(false, false, false, "unsupported", null);
                ShowMessage($"Notifications could not be prepared: {exception.Message}", false);
            }

            StateHasChanged();
            return;
        }

        // The Enable button is rendered only while this device is not subscribed; bind each new instance.
        if (pushModule is not null && publicKey is not null && dotNetReference is not null
            && enableButton.Context is not null && !enableButton.Equals(boundButton))
        {
            boundButton = enableButton;
            await pushModule.InvokeVoidAsync("bindSubscribeButton", enableButton, publicKey, dotNetReference);
        }
    }

    [JSInvokable]
    public async Task OnSubscribed(BrowserSubscription subscription)
    {
        try
        {
            await Sender.Send(new SavePushDeviceUsecase(
                subscription.Endpoint,
                subscription.P256dh,
                subscription.Auth,
                subscription.Name));
            browserState = browserState! with { Endpoint = subscription.Endpoint, Permission = "granted" };
            await LoadDevicesAsync();
            ShowMessage("Notifications are on for this device. Send a test to check delivery.", true);
        }
        catch (Exception exception)
        {
            ShowMessage($"This device could not be saved: {exception.Message}", false);
        }

        StateHasChanged();
    }

    [JSInvokable]
    public Task OnSubscribeFailed(string error)
    {
        ShowMessage(error, false);
        StateHasChanged();
        return Task.CompletedTask;
    }

    private async Task LoadAsync()
    {
        loading = true;
        try
        {
            var settings = await SettingsRepository.GetByIdAsync(
                Samwise.Domain.Notifications.NotificationSettings.DocumentId)
                ?? new Samwise.Domain.Notifications.NotificationSettings();
            notifyPlanAwaitingApproval = settings.NotifyPlanAwaitingApproval;
            notifyQuestionAsked = settings.NotifyQuestionAsked;
            notifyNeedsAttention = settings.NotifyNeedsAttention;
            notifyCompleted = settings.NotifyCompleted;
            notifyDailyAutoExecutionSummary = settings.NotifyDailyAutoExecutionSummary;
            await LoadDevicesAsync();
        }
        catch (Exception exception)
        {
            ShowMessage($"The notification settings could not be loaded: {exception.Message}", false);
        }
        finally
        {
            loading = false;
        }
    }

    private async Task LoadDevicesAsync() =>
        devices = await DeviceRepository.ListAsync(new PushDevicesSpecification());

    private async Task SaveSettingsAsync()
    {
        busy = true;
        try
        {
            await Sender.Send(new SaveNotificationSettingsUsecase(
                notifyPlanAwaitingApproval,
                notifyQuestionAsked,
                notifyNeedsAttention,
                notifyCompleted,
                notifyDailyAutoExecutionSummary));
            ShowMessage("Notification settings saved.", true);
        }
        catch (Exception exception)
        {
            ShowMessage(exception.Message, false);
        }
        finally
        {
            busy = false;
        }
    }

    private async Task RemoveAsync(PushDevice device)
    {
        busy = true;
        try
        {
            if (device.Id == ThisDevice?.Id && pushModule is not null)
            {
                await pushModule.InvokeAsync<string?>("unsubscribe");
                browserState = browserState! with { Endpoint = null };
            }

            await Sender.Send(new RemovePushDeviceUsecase(device.Id));
            await LoadDevicesAsync();
            ShowMessage($"{device.Name} will no longer receive notifications.", true);
        }
        catch (Exception exception)
        {
            ShowMessage(exception.Message, false);
        }
        finally
        {
            busy = false;
        }
    }

    private async Task SendTestAsync()
    {
        sendingTest = true;
        try
        {
            var result = await Sender.Send(new SendTestNotificationUsecase());
            await LoadDevicesAsync();
            var message = $"Test sent to {result.Delivered} device(s).";
            if (result.Removed > 0)
            {
                message += $" {result.Removed} expired device(s) removed.";
            }

            ShowMessage(result.Errors.Count == 0 ? message : $"{message} {string.Join(" ", result.Errors)}", result.Errors.Count == 0);
        }
        catch (Exception exception)
        {
            ShowMessage(exception.Message, false);
        }
        finally
        {
            sendingTest = false;
        }
    }

    private void ShowMessage(string message, bool success)
    {
        pageMessage = message;
        pageMessageIsSuccess = success;
    }

    private static string DeviceMark(PushDevice device) =>
        device.Name.StartsWith("iPhone", StringComparison.Ordinal) || device.Name.StartsWith("Android", StringComparison.Ordinal)
            ? "▯"
            : "▭";

    public async ValueTask DisposeAsync()
    {
        dotNetReference?.Dispose();
        if (pushModule is not null)
        {
            try
            {
                await pushModule.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The circuit is already gone.
            }
        }
    }

    public sealed record BrowserPushState(
        bool Supported,
        bool Standalone,
        bool AppleMobile,
        string Permission,
        string? Endpoint);

    public sealed record BrowserSubscription(string Endpoint, string P256dh, string Auth, string Name);
}

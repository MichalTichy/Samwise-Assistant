using Samwise.Domain.Notifications;

namespace Samwise.Logic.Notifications.Services;

public sealed record PushNotification(string Title, string Body, string Url, string Tag, bool Urgent);

public enum PushDeliveryStatus
{
    Delivered,
    /// <summary>The push service no longer knows the subscription (HTTP 404/410); remove the device.</summary>
    Expired,
    Failed
}

public sealed record PushDeliveryResult(PushDeliveryStatus Status, string? Error = null);

/// <summary>Web Push transport port, implemented by <c>Samwise.Infrastructure.WebPush</c>.</summary>
public interface IPushNotificationSender
{
    Task<PushDeliveryResult> SendAsync(
        PushDevice device,
        PushNotification notification,
        CancellationToken cancellationToken);
}

/// <summary>Supplies the VAPID public key browsers need to create a subscription.</summary>
public interface IPushApplicationKeyProvider
{
    Task<string> GetPublicKeyAsync(CancellationToken cancellationToken);
}

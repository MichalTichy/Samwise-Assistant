using System.Net;
using System.Text.Json;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Samwise.Domain.Notifications;
using Samwise.Infrastructure.WebPush.Keys;
using Samwise.Logic.Notifications;
using Samwise.Logic.Notifications.Services;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Infrastructure.WebPush.Sending;

internal sealed class WebPushNotificationSender(
    PushServiceClient client,
    IReadOnlyRepository<PushApplicationKeys> keyRepository,
    VapidKeyProtector keyProtector,
    IOptions<WebPushOptions> options,
    IConfiguration configuration) : IPushNotificationSender, IPushApplicationKeyProvider
{
    private static readonly TimeSpan TimeToLive = TimeSpan.FromHours(24);

    public async Task<string> GetPublicKeyAsync(CancellationToken cancellationToken) =>
        (await LoadKeysAsync(cancellationToken)).PublicKey;

    public async Task<PushDeliveryResult> SendAsync(
        PushDevice device,
        PushNotification notification,
        CancellationToken cancellationToken)
    {
        var subject = ResolveSubject();
        if (subject is null)
        {
            return new PushDeliveryResult(
                PushDeliveryStatus.Failed,
                "Set WebPush:Subject (or OwnerAccount:Email) to a mailto: or https: contact; Apple rejects pushes without it.");
        }

        var keys = await LoadKeysAsync(cancellationToken);
        var authentication = new VapidAuthentication(keys.PublicKey, keyProtector.Unprotect(keys.ProtectedPrivateKey))
        {
            Subject = subject
        };
        var subscription = new PushSubscription { Endpoint = device.Endpoint };
        subscription.SetKey(PushEncryptionKeyName.P256DH, device.P256dh);
        subscription.SetKey(PushEncryptionKeyName.Auth, device.Auth);
        var message = new PushMessage(Serialize(notification))
        {
            TimeToLive = (int)TimeToLive.TotalSeconds,
            Urgency = notification.Urgent ? PushMessageUrgency.High : PushMessageUrgency.Normal
        };

        try
        {
            await client.RequestPushMessageDeliveryAsync(subscription, message, authentication, cancellationToken);
            return new PushDeliveryResult(PushDeliveryStatus.Delivered);
        }
        catch (PushServiceClientException exception)
            when (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return new PushDeliveryResult(PushDeliveryStatus.Expired);
        }
        catch (PushServiceClientException exception)
        {
            return new PushDeliveryResult(
                PushDeliveryStatus.Failed,
                $"The push service returned {(int)exception.StatusCode}: {exception.Message}");
        }
    }

    internal static string Serialize(PushNotification notification) =>
        JsonSerializer.Serialize(new
        {
            title = notification.Title,
            body = notification.Body,
            url = notification.Url,
            tag = notification.Tag
        });

    private string? ResolveSubject()
    {
        var subject = options.Value.Subject;
        if (string.IsNullOrWhiteSpace(subject))
        {
            var ownerEmail = configuration["OwnerAccount:Email"];
            subject = string.IsNullOrWhiteSpace(ownerEmail) ? null : $"mailto:{ownerEmail.Trim()}";
        }

        return subject is not null
            && (subject.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                || subject.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                ? subject.Trim()
                : null;
    }

    private async Task<PushApplicationKeys> LoadKeysAsync(CancellationToken cancellationToken) =>
        await keyRepository.GetByIdAsync(PushApplicationKeys.DocumentId, cancellationToken)
        ?? throw new InvalidOperationException("The Web Push application keys have not been created yet.");
}

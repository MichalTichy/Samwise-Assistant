using Microsoft.Extensions.Logging;
using Samwise.Domain.Notifications;
using Samwise.Logic.Notifications.Specifications;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Notifications.Services;

public sealed record PushBroadcastResult(int Delivered, int Removed, IReadOnlyList<string> Errors);

public interface IPushNotificationService
{
    Task<PushBroadcastResult> SendToAllAsync(PushNotification notification, CancellationToken cancellationToken);
}

internal sealed class PushNotificationService(
    IRepository<PushDevice> deviceRepository,
    IPushNotificationSender sender,
    TimeProvider timeProvider,
    ILogger<PushNotificationService> logger) : IPushNotificationService
{
    public async Task<PushBroadcastResult> SendToAllAsync(
        PushNotification notification,
        CancellationToken cancellationToken)
    {
        var devices = await deviceRepository.ListAsync(new PushDevicesSpecification(), cancellationToken);
        var delivered = 0;
        var removed = 0;
        var errors = new List<string>();
        foreach (var device in devices)
        {
            PushDeliveryResult result;
            try
            {
                result = await sender.SendAsync(device, notification, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                result = new PushDeliveryResult(PushDeliveryStatus.Failed, exception.Message);
            }

            switch (result.Status)
            {
                case PushDeliveryStatus.Delivered:
                    delivered++;
                    device.RecordDelivered(timeProvider.GetUtcNow());
                    await deviceRepository.UpdateAsync(device, cancellationToken);
                    break;
                case PushDeliveryStatus.Expired:
                    removed++;
                    await deviceRepository.DeleteAsync(device, cancellationToken);
                    break;
                default:
                    var error = result.Error ?? "The push service rejected the notification.";
                    errors.Add($"{device.Name}: {error}");
                    logger.LogWarning("Push notification to device {DeviceName} failed: {Error}", device.Name, error);
                    device.RecordFailed(error, timeProvider.GetUtcNow());
                    await deviceRepository.UpdateAsync(device, cancellationToken);
                    break;
            }
        }

        return new PushBroadcastResult(delivered, removed, errors);
    }
}

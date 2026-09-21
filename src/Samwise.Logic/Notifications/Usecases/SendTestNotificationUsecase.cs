using MediatR;
using Samwise.Logic.Notifications.Services;

namespace Samwise.Logic.Notifications.Usecases;

public sealed record SendTestNotificationUsecase : IRequest<PushBroadcastResult>;

internal sealed class SendTestNotificationUsecaseHandler(IPushNotificationService pushService)
    : IRequestHandler<SendTestNotificationUsecase, PushBroadcastResult>
{
    public Task<PushBroadcastResult> Handle(SendTestNotificationUsecase request, CancellationToken cancellationToken) =>
        pushService.SendToAllAsync(
            new PushNotification(
                "Samwise notifications work",
                "You will be notified here when a plan or question needs you.",
                "/settings/notifications",
                "samwise-test",
                Urgent: true),
            cancellationToken);
}

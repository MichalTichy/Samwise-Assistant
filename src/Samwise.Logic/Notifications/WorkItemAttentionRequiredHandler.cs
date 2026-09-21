using MediatR;
using Samwise.Domain.Notifications;
using Samwise.Logic.Notifications.Services;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Notifications;

internal sealed class WorkItemAttentionRequiredHandler(
    IReadOnlyRepository<NotificationSettings> settingsRepository,
    IPushNotificationService pushService) : INotificationHandler<WorkItemAttentionRequired>
{
    private const int MaximumBodyLength = 180;

    public async Task Handle(WorkItemAttentionRequired notification, CancellationToken cancellationToken)
    {
        var settings = await settingsRepository.GetByIdAsync(NotificationSettings.DocumentId, cancellationToken)
            ?? new NotificationSettings();
        if (!settings.IsEnabled(notification.Kind))
        {
            return;
        }

        await pushService.SendToAllAsync(CreateMessage(notification), cancellationToken);
    }

    internal static PushNotification CreateMessage(WorkItemAttentionRequired notification)
    {
        var title = notification.Kind switch
        {
            WorkItemAttentionKind.PlanAwaitingApproval => "Plan ready for approval",
            WorkItemAttentionKind.QuestionAsked => "Samwise has a question",
            WorkItemAttentionKind.NeedsAttention => "Work item needs attention",
            WorkItemAttentionKind.OutcomeUnknown => "Execution outcome is unknown",
            WorkItemAttentionKind.Completed => "Work item completed",
            _ => "Samwise"
        };
        var body = string.IsNullOrWhiteSpace(notification.Detail)
            ? notification.WorkItemTitle
            : $"{notification.WorkItemTitle}: {notification.Detail.Trim()}";
        if (body.Length > MaximumBodyLength)
        {
            body = body[..(MaximumBodyLength - 1)] + "…";
        }

        return new PushNotification(
            title,
            body,
            $"/work-items/{Uri.EscapeDataString(notification.WorkItemId)}",
            // One notification per work item: a newer state replaces the older banner.
            $"workitem-{notification.WorkItemId}",
            notification.Kind is WorkItemAttentionKind.PlanAwaitingApproval or WorkItemAttentionKind.QuestionAsked);
    }
}

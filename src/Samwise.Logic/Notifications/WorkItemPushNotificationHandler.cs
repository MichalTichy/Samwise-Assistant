using MediatR;
using Samwise.Domain.Notifications;
using Samwise.Domain.WorkItems.Events;
using Samwise.Logic.Notifications.Services;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Notifications;

/// <summary>Pushes the work item facts that need the owner to every enabled device.</summary>
internal sealed class WorkItemPushNotificationHandler(
    IReadOnlyRepository<NotificationSettings> settingsRepository,
    IPushNotificationService pushService) :
    INotificationHandler<PlanProposed>,
    INotificationHandler<PlanningQuestionAsked>,
    INotificationHandler<PlanningFailed>,
    INotificationHandler<ExecutionFailed>,
    INotificationHandler<ExecutionOutcomeUnknown>,
    INotificationHandler<ExecutionCompleted>
{
    private const int MaximumBodyLength = 180;

    public Task Handle(PlanProposed notification, CancellationToken cancellationToken) =>
        SendAsync(notification, settings => settings.NotifyPlanAwaitingApproval, "Plan ready for approval", null, true, cancellationToken);

    public Task Handle(PlanningQuestionAsked notification, CancellationToken cancellationToken) =>
        SendAsync(notification, settings => settings.NotifyQuestionAsked, "Samwise has a question", notification.Prompt, true, cancellationToken);

    public Task Handle(PlanningFailed notification, CancellationToken cancellationToken) =>
        SendAsync(notification, settings => settings.NotifyNeedsAttention, "Work item needs attention", notification.Reason, false, cancellationToken);

    public Task Handle(ExecutionFailed notification, CancellationToken cancellationToken) =>
        SendAsync(notification, settings => settings.NotifyNeedsAttention, "Work item needs attention", notification.Reason, false, cancellationToken);

    public Task Handle(ExecutionOutcomeUnknown notification, CancellationToken cancellationToken) =>
        SendAsync(notification, settings => settings.NotifyNeedsAttention, "Execution outcome is unknown", notification.Reason, false, cancellationToken);

    public Task Handle(ExecutionCompleted notification, CancellationToken cancellationToken) =>
        SendAsync(notification, settings => settings.NotifyCompleted, "Work item completed", null, false, cancellationToken);

    private async Task SendAsync(
        WorkItemEvent workItemEvent,
        Func<NotificationSettings, bool> isEnabled,
        string title,
        string? detail,
        bool urgent,
        CancellationToken cancellationToken)
    {
        var settings = await settingsRepository.GetByIdAsync(NotificationSettings.DocumentId, cancellationToken)
            ?? new NotificationSettings();
        if (isEnabled(settings))
        {
            await pushService.SendToAllAsync(CreateMessage(workItemEvent, title, detail, urgent), cancellationToken);
        }
    }

    internal static PushNotification CreateMessage(WorkItemEvent workItemEvent, string title, string? detail, bool urgent)
    {
        var body = string.IsNullOrWhiteSpace(detail)
            ? workItemEvent.WorkItemTitle
            : $"{workItemEvent.WorkItemTitle}: {detail.Trim()}";
        if (body.Length > MaximumBodyLength)
        {
            body = body[..(MaximumBodyLength - 1)] + "…";
        }

        return new PushNotification(
            title,
            body,
            $"/work-items/{Uri.EscapeDataString(workItemEvent.WorkItemId)}",
            // One notification per work item: a newer state replaces the older banner.
            $"workitem-{workItemEvent.WorkItemId}",
            urgent);
    }
}

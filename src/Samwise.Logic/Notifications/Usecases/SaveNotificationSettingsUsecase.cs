using MediatR;
using Samwise.Domain.Notifications;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Notifications.Usecases;

public sealed record SaveNotificationSettingsUsecase(
    bool NotifyPlanAwaitingApproval,
    bool NotifyQuestionAsked,
    bool NotifyNeedsAttention,
    bool NotifyCompleted) : IRequest;

internal sealed class SaveNotificationSettingsUsecaseHandler(IRepository<NotificationSettings> repository)
    : IRequestHandler<SaveNotificationSettingsUsecase>
{
    public async Task Handle(SaveNotificationSettingsUsecase request, CancellationToken cancellationToken)
    {
        var settings = await repository.GetRequiredByIdAsync(NotificationSettings.DocumentId, cancellationToken);
        settings.NotifyPlanAwaitingApproval = request.NotifyPlanAwaitingApproval;
        settings.NotifyQuestionAsked = request.NotifyQuestionAsked;
        settings.NotifyNeedsAttention = request.NotifyNeedsAttention;
        settings.NotifyCompleted = request.NotifyCompleted;
        await repository.UpdateAsync(settings, cancellationToken);
    }
}

using MediatR;
using Samwise.Domain.Notifications;
using Samwise.Logic.Notifications.Services;
using Samwise.Logic.Notifications.Specifications;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;

namespace Samwise.Logic.Notifications.Usecases;

internal sealed class SendDailyAutoExecutionSummaryUsecaseHandler(
    IReadOnlyRepository<NotificationSettings> settingsRepository,
    IReadOnlyRepository<DomainWorkItem> workItemRepository,
    IRepository<DailyAutoExecutionDigest> digestRepository,
    IPushNotificationService pushService,
    TimeProvider timeProvider) : IRequestHandler<SendDailyAutoExecutionSummaryUsecase, int>
{
    public async Task<int> Handle(SendDailyAutoExecutionSummaryUsecase request, CancellationToken cancellationToken)
    {
        var settings = await settingsRepository.GetByIdAsync(NotificationSettings.DocumentId, cancellationToken);
        if (settings?.NotifyDailyAutoExecutionSummary == false)
        {
            return 0;
        }

        var digestId = DailyAutoExecutionDigest.IdFor(request.ReportDate);
        if (await digestRepository.GetByIdAsync(digestId, cancellationToken) is not null)
        {
            return 0;
        }

        var (from, until) = AutoExecutionReportPeriod.ForDay(request.ReportDate);
        var completed = await workItemRepository.ListAsync(
            new AutoExecutedWorkItemsSpecification(from, until), cancellationToken);
        if (completed.Count == 0)
        {
            return 0;
        }

        var body = string.Join(" · ", completed.Take(2).Select(workItem => workItem.Title));
        if (completed.Count > 2)
        {
            body += $" · and {completed.Count - 2} more";
        }

        if (body.Length > 180)
        {
            body = body[..179] + "…";
        }

        var result = await pushService.SendToAllAsync(
            new PushNotification(
                completed.Count == 1 ? "1 work item handled automatically" : $"{completed.Count} work items handled automatically",
                body,
                $"/work-items/auto-executions?date={request.ReportDate:yyyy-MM-dd}",
                $"auto-execution-digest-{request.ReportDate:yyyy-MM-dd}",
                Urgent: false),
            cancellationToken);

        if (result.Errors.Count > 0 && result.Delivered == 0)
        {
            throw new InvalidOperationException("The daily auto-execution summary could not be delivered to any device.");
        }

        await digestRepository.AddAsync(new DailyAutoExecutionDigest
        {
            Id = digestId,
            ReportDate = request.ReportDate,
            SentAt = timeProvider.GetUtcNow()
        }, cancellationToken);

        return completed.Count;
    }
}

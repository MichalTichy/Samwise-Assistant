using MediatR;
using Samwise.Logic.Notifications;
using Samwise.Logic.Notifications.Usecases;

namespace Samwise.Web.Notifications;

internal sealed class DailyAutoExecutionSummaryBackgroundService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<DailyAutoExecutionSummaryBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15), timeProvider);
        try
        {
            do
            {
                await SendIfDueAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown.
        }
    }

    private async Task SendIfDueAsync(CancellationToken cancellationToken)
    {
        var reportDate = DailyAutoExecutionSchedule.ReportDateDue(timeProvider.GetUtcNow());
        if (reportDate is null)
        {
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await sender.Send(new SendDailyAutoExecutionSummaryUsecase(reportDate.Value), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Sending auto-execution summary for {ReportDate} failed", reportDate);
        }
    }

}

using MediatR;
using Samwise.Logic.Tasks.Usecases;

namespace Samwise.Web.Tasks;

internal sealed class ScheduledTasksBackgroundService(IServiceScopeFactory scopes,
    ILogger<ScheduledTasksBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Allow application startup/schema initialization to finish before the first tick.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ISender>()
                    .Send(new ProcessScheduledTasksUsecase(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "Scheduled task processing failed"); }
        }
    }
}

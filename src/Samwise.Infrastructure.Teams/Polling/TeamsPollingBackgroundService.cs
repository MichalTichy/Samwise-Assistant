using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Samwise.Inputs.Teams;
using Samwise.Inputs.Teams.Usecases;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Infrastructure.Teams.Polling;

internal sealed class TeamsPollingBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<TeamsPollingBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = await LoadIntervalAsync(stoppingToken);
            await Task.Delay(interval, stoppingToken);
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();
                var result = await sender.Send(new ProcessTeamsInputsUsecase(), stoppingToken);
                if (result.Errors.Count > 0)
                {
                    logger.LogWarning(
                        "Automatic Teams processing finished with {ErrorCount} errors",
                        result.Errors.Count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Automatic Teams processing failed");
            }
        }
    }

    private async Task<TimeSpan> LoadIntervalAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<TeamsInputConfiguration>>();
            var configuration = await repository.GetByIdAsync(
                TeamsInputConfiguration.DocumentId,
                cancellationToken);
            return TimeSpan.FromMinutes(
                configuration?.PollingIntervalMinutes
                ?? TeamsInputConfiguration.DefaultPollingIntervalMinutes);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Loading the Teams polling interval failed");
            return TimeSpan.FromMinutes(TeamsInputConfiguration.DefaultPollingIntervalMinutes);
        }
    }
}

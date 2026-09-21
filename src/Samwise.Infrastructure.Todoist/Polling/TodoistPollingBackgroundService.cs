using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Samwise.Inputs.Todoist.Configuration;
using Samwise.Inputs.Todoist.Usecases.Processing;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Infrastructure.Todoist.Polling;

internal sealed class TodoistPollingBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<TodoistPollingBackgroundService> logger) : BackgroundService
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
                var result = await sender.Send(new ProcessTodoistInputsUsecase(), stoppingToken);
                if (result.Errors.Count > 0)
                {
                    logger.LogWarning(
                        "Automatic Todoist processing finished with {ErrorCount} errors",
                        result.Errors.Count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Automatic Todoist processing failed");
            }
        }
    }

    private async Task<TimeSpan> LoadIntervalAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<TodoistInputConfiguration>>();
            var configuration = await repository.GetByIdAsync(
                TodoistInputConfiguration.DocumentId,
                cancellationToken);
            return TimeSpan.FromMinutes(
                configuration?.PollingIntervalMinutes
                ?? TodoistInputConfiguration.DefaultPollingIntervalMinutes);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Loading the Todoist polling interval failed");
            return TimeSpan.FromMinutes(TodoistInputConfiguration.DefaultPollingIntervalMinutes);
        }
    }
}

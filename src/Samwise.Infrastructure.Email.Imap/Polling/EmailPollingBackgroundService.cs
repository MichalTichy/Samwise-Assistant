using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Samwise.Inputs.Email;
using Samwise.Inputs.Email.Usecases;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Infrastructure.Email.Imap.Polling;

internal sealed class EmailPollingBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<EmailPollingBackgroundService> logger) : BackgroundService
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
                var result = await sender.Send(
                    new ProcessEmailsUsecase(EmailSelection.NewSinceCheckpoint),
                    stoppingToken);
                if (result.Errors.Count > 0)
                {
                    logger.LogWarning(
                        "Automatic email processing finished with {ErrorCount} account errors",
                        result.Errors.Count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Automatic email processing failed");
            }
        }
    }

    private async Task<TimeSpan> LoadIntervalAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider
                .GetRequiredService<IReadOnlyRepository<EmailInputConfiguration>>();
            var configuration = await repository.GetByIdAsync(
                EmailInputConfiguration.DocumentId,
                cancellationToken);
            return TimeSpan.FromMinutes(
                configuration?.PollingIntervalMinutes
                ?? EmailInputConfiguration.DefaultPollingIntervalMinutes);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Loading the email polling interval failed");
            return TimeSpan.FromMinutes(EmailInputConfiguration.DefaultPollingIntervalMinutes);
        }
    }
}

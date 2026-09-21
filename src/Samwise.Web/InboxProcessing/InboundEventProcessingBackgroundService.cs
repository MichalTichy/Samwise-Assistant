using MediatR;
using Samwise.Domain.Inbox;
using Samwise.Logic.Inbox.Services;
using Samwise.Logic.Inbox.Specifications;
using Samwise.Logic.Inbox.Usecases;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Web.InboxProcessing;

internal sealed class InboundEventProcessingBackgroundService(
    IInboundEventProcessingQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<InboundEventProcessingBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverPendingAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(1));
            try
            {
                var inboundEventId = await queue.DequeueAsync(timeout.Token);
                await ProcessAsync(inboundEventId, stoppingToken);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                await RecoverPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task RecoverPendingAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<InboundEvent>>();
            var pending = await repository.ListAsync(new PendingInboundEventsSpecification(), cancellationToken);
            foreach (var inboundEvent in pending)
            {
                queue.Enqueue(inboundEvent.Id);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Recovering pending inbound events failed");
        }
    }

    private async Task ProcessAsync(string inboundEventId, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await sender.Send(new ProcessCapturedInboundEventUsecase(inboundEventId), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown leaves the event pending for recovery on restart.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Processing inbound event {InboundEventId} failed", inboundEventId);
        }
    }
}

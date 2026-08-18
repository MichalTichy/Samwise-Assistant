using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Samwise.Infrastructure.Persistence.Marten.Agents;

internal sealed class AgentWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<AgentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Samwise agent worker started");
        await using (var recoveryScope = scopeFactory.CreateAsyncScope())
        {
            var processor = recoveryScope.ServiceProvider.GetRequiredService<AgentWorkProcessor>();
            await processor.RecoverAbandonedWorkAsync(stoppingToken);
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = false;
            do
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<AgentWorkProcessor>();
                processed = await processor.ProcessNextAsync(stoppingToken);
            }
            while (processed && !stoppingToken.IsCancellationRequested);

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                break;
            }
        }
    }
}

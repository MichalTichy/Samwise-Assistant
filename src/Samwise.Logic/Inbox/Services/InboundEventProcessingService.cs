using Samwise.Logic.WorkItems.Services;

namespace Samwise.Logic.Inbox.Services;

internal interface IInboundEventProcessingService
{
    Task ProcessAsync(string inboundEventId, CancellationToken cancellationToken);
}

internal sealed class InboundEventProcessingService(
    IInboundEventClassificationService classificationService,
    IWorkItemPlanningService planningService) : IInboundEventProcessingService
{
    public async Task ProcessAsync(string inboundEventId, CancellationToken cancellationToken)
    {
        var workItemIds = await classificationService.ClassifyAsync(inboundEventId, cancellationToken);
        foreach (var workItemId in workItemIds.Distinct(StringComparer.Ordinal))
        {
            await planningService.PlanAsync(workItemId, cancellationToken);
        }
    }
}

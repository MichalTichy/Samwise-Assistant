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
        var workItemId = await classificationService.ClassifyAsync(inboundEventId, cancellationToken);
        if (workItemId is not null)
        {
            await planningService.PlanAsync(workItemId, cancellationToken);
        }
    }
}

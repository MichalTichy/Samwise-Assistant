using Samwise.Logic.WorkItems.Services;
using Samwise.Domain.Inbox;
using Samwise.Domain.Tasks;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Inbox.Services;

internal interface IInboundEventProcessingService
{
    Task ProcessAsync(string inboundEventId, CancellationToken cancellationToken);
}

internal sealed class InboundEventProcessingService(
    IInboundEventClassificationService classificationService,
    IWorkItemPlanningService planningService,
    IRepository<InboundEvent>? inputs = null) : IInboundEventProcessingService
{
    public async Task ProcessAsync(string inboundEventId, CancellationToken cancellationToken)
    {
        if (inputs is not null && await inputs.GetByIdAsync(inboundEventId, cancellationToken) is { Source: "Todoist", Status: InboundEventStatus.Pending } legacy)
        {
            legacy.Status = InboundEventStatus.Processed;
            legacy.HandledAt = DateTimeOffset.UtcNow;
            legacy.Explanation = "Todoist is now a task source. Legacy input retained for history; no new Intake work created.";
            await inputs.UpdateAsync(legacy, cancellationToken);
            return;
        }
        if (inputs is not null && await inputs.GetByIdAsync(inboundEventId, cancellationToken) is ScheduledTaskInboundEvent scheduled)
        {
            if (scheduled.Status != InboundEventStatus.Pending) return;
            await planningService.PlanAsync(scheduled.WorkItemId, cancellationToken);
            scheduled.Status = InboundEventStatus.Processed;
            scheduled.HandledAt = DateTimeOffset.UtcNow;
            scheduled.Explanation = "Internal task submitted to planning.";
            await inputs.UpdateAsync(scheduled, cancellationToken);
            return;
        }
        var workItemIds = await classificationService.ClassifyAsync(inboundEventId, cancellationToken);
        foreach (var workItemId in workItemIds.Distinct(StringComparer.Ordinal))
        {
            await planningService.PlanAsync(workItemId, cancellationToken);
        }
    }
}

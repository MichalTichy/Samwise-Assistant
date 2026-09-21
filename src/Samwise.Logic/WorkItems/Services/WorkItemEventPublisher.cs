using MediatR;
using Microsoft.Extensions.Logging;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;

namespace Samwise.Logic.WorkItems.Services;

public interface IWorkItemEventPublisher
{
    /// <summary>
    /// Publishes the facts <paramref name="workItem"/> recorded since the last publish. Call it after the
    /// change is persisted (after the unit of work commits). Handlers are independent reactions: a failing
    /// handler is logged and never fails the operation that already happened.
    /// </summary>
    Task PublishAsync(DomainWorkItem workItem);
}

internal sealed class WorkItemEventPublisher(
    IPublisher publisher,
    ILogger<WorkItemEventPublisher> logger) : IWorkItemEventPublisher
{
    public async Task PublishAsync(DomainWorkItem workItem)
    {
        foreach (var workItemEvent in workItem.DequeueEvents())
        {
            try
            {
                // The fact is already committed; a caller's cancellation must not drop it.
                await publisher.Publish(workItemEvent, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Handling {EventType} for work item {WorkItemId} failed",
                    workItemEvent.GetType().Name,
                    workItemEvent.WorkItemId);
            }
        }
    }
}

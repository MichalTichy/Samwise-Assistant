using MediatR;
using Microsoft.Extensions.Logging;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;

namespace Samwise.Logic.Notifications.Services;

public interface IWorkItemAttentionNotifier
{
    /// <summary>
    /// Publishes <see cref="WorkItemAttentionRequired"/> for the work item's current status; call it after
    /// persisting a transition. Failures are logged, never thrown.
    /// </summary>
    Task NotifyAsync(DomainWorkItem workItem, string? detail = null);
}

internal sealed class WorkItemAttentionNotifier(
    IPublisher publisher,
    ILogger<WorkItemAttentionNotifier> logger) : IWorkItemAttentionNotifier
{
    public async Task NotifyAsync(DomainWorkItem workItem, string? detail = null)
    {
        try
        {
            // The transition is already committed; a caller's cancellation must not drop the notice.
            await publisher.Publish(
                new WorkItemAttentionRequired(workItem.Id, workItem.Status, workItem.Title, detail),
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Notifying about work item {WorkItemId} ({Status}) failed",
                workItem.Id,
                workItem.Status);
        }
    }
}

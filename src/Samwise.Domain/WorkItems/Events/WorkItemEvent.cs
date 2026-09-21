using MediatR;

namespace Samwise.Domain.WorkItems.Events;

/// <summary>
/// A fact about a work item. <see cref="WorkItem"/> records one for every state transition; the caller
/// publishes them after the change is persisted, and anyone interested handles the concrete event type.
/// </summary>
public abstract record WorkItemEvent(string WorkItemId, string WorkItemTitle, DateTimeOffset OccurredAt) : INotification;

using MediatR;
using Samwise.Domain.WorkItems;

namespace Samwise.Logic.Notifications;

/// <summary>
/// Published after a work item transition to <paramref name="Status"/> has been persisted and the owner may need to act.
/// Handlers are independent reactions: they must never change the workflow outcome.
/// </summary>
public sealed record WorkItemAttentionRequired(
    string WorkItemId,
    WorkItemStatus Status,
    string WorkItemTitle,
    string? Detail) : INotification;

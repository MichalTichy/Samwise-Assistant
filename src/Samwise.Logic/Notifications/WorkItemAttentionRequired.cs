using MediatR;
using Samwise.Domain.Notifications;

namespace Samwise.Logic.Notifications;

/// <summary>
/// Published after a work item transition has been persisted and the owner may need to act.
/// Handlers are independent reactions: they must never change the workflow outcome.
/// </summary>
public sealed record WorkItemAttentionRequired(
    string WorkItemId,
    WorkItemAttentionKind Kind,
    string WorkItemTitle,
    string? Detail) : INotification;

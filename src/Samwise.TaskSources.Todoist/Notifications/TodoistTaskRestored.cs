using MediatR;

namespace Samwise.TaskSources.Todoist.Notifications;

/// <summary>A removed Todoist task is eligible for import again on the next synchronization.</summary>
public sealed record TodoistTaskRestored(string TodoistTaskId) : INotification;

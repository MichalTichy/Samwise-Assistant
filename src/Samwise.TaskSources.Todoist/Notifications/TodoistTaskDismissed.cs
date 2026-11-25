using MediatR;

namespace Samwise.TaskSources.Todoist.Notifications;

/// <summary>The owner removed a Todoist task, including all future occurrences, from Samwise.</summary>
public sealed record TodoistTaskDismissed(string TodoistTaskId, string? AgentTaskId) : INotification;

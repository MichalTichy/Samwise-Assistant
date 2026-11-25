using MediatR;

namespace Samwise.TaskSources.Todoist.Usecases.Dismissal;

/// <summary>Removes the Todoist task behind an imported Samwise task, with every future occurrence. Todoist is not changed.</summary>
public sealed record DismissTodoistTaskUsecase(string AgentTaskId) : IRequest<TodoistTaskDismissalResult>;

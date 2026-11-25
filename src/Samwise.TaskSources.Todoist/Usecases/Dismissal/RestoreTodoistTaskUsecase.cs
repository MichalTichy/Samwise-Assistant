using MediatR;

namespace Samwise.TaskSources.Todoist.Usecases.Dismissal;

/// <summary>Lifts an owner removal; the next synchronization imports the Todoist task in the normal way.</summary>
public sealed record RestoreTodoistTaskUsecase(string TodoistTaskId) : IRequest<TodoistTaskDismissalResult>;

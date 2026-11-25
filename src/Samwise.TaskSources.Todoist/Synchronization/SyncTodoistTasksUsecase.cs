using MediatR;
using Samwise.TaskSources.Todoist.Synchronization;

namespace Samwise.TaskSources.Todoist.Synchronization;

public sealed record SyncTodoistTasksUsecase(string? ExternalTaskId = null) : IRequest<TodoistSynchronizationResult>;

using MediatR;
using Samwise.Inputs.Todoist.Ingestion;

namespace Samwise.Inputs.Todoist.Tasks;

public sealed record SyncTodoistTasksUsecase(string? ExternalTaskId = null) : IRequest<TodoistProcessingResult>;

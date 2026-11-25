using MediatR;
using Samwise.Domain.Tasks;

namespace Samwise.Logic.Tasks.Usecases;

public sealed record SaveAgentTaskUsecase(string? Id, int ExpectedVersion, string Title,
    string Instructions, IReadOnlyList<TaskChecklistItem> Checklist, DateTimeOffset? NextRunAt,
    string? OriginWorkItemId = null, bool IsOwnerEdit = true, string? ExecutingWorkItemId = null) : IRequest<AgentTask>;

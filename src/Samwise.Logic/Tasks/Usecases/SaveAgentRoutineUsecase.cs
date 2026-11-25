using MediatR;
using Samwise.Domain.Tasks;

namespace Samwise.Logic.Tasks.Usecases;

public sealed record SaveAgentRoutineUsecase(string? Id, int ExpectedVersion, string Title,
    string Instructions, IReadOnlyList<TaskChecklistItem> Checklist, string Cron,
    string TimeZoneId, bool IsEnabled) : IRequest<AgentRoutine>;

using MediatR;
using Samwise.Domain.Tasks;

namespace Samwise.Logic.Tasks.Usecases;

public sealed record MaterializeTaskOccurrenceUsecase(string RoutineId, DateTimeOffset OccurrenceAt) : IRequest<AgentTask>;

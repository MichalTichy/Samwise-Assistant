using MediatR;

namespace Samwise.Domain.Tasks;

public sealed record AgentRoutineChanged(string RoutineId) : INotification;

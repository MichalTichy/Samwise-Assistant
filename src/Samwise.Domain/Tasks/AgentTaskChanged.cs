using MediatR;

namespace Samwise.Domain.Tasks;

public sealed record AgentTaskChanged(string TaskId) : INotification;

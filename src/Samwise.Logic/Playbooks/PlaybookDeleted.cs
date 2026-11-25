using MediatR;

namespace Samwise.Logic.Playbooks;

public sealed record PlaybookDeleted(string PlaybookId) : INotification;

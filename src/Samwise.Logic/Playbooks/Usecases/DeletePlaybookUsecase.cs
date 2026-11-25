using MediatR;

namespace Samwise.Logic.Playbooks.Usecases;

public sealed record DeletePlaybookUsecase(string PlaybookId) : IRequest<bool>;

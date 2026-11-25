using MediatR;
using Samwise.Domain.Playbooks;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Playbooks.Usecases;

internal sealed class DeletePlaybookUsecaseHandler(
    IRepository<Playbook> repository,
    IPublisher publisher) : IRequestHandler<DeletePlaybookUsecase, bool>
{
    public async Task<bool> Handle(DeletePlaybookUsecase request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlaybookId))
        {
            return false;
        }

        var playbook = await repository.GetByIdAsync(request.PlaybookId, cancellationToken);
        if (playbook is null)
        {
            return false;
        }

        await repository.DeleteAsync(playbook, cancellationToken);
        await publisher.Publish(new PlaybookDeleted(playbook.Id), cancellationToken);
        return true;
    }
}

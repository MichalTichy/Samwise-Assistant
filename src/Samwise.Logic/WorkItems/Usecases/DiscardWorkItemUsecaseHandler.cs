using MediatR;
using Samwise.Logic.WorkItems.Services;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;
using Task = System.Threading.Tasks.Task;

namespace Samwise.Logic.WorkItems.Usecases;

internal sealed class DiscardWorkItemUsecaseHandler(
    IRepository<DomainWorkItem> workItemRepository,
    IWorkItemEventPublisher eventPublisher,
    TimeProvider timeProvider) : IRequestHandler<DiscardWorkItemUsecase>
{
    public async Task Handle(DiscardWorkItemUsecase request, CancellationToken cancellationToken)
    {
        var workItem = await workItemRepository.GetRequiredByIdAsync(request.WorkItemId, cancellationToken);
        workItem.Discard(timeProvider.GetUtcNow(), reason: request.Reason);
        await workItemRepository.UpdateAsync(workItem, cancellationToken);
        await eventPublisher.PublishAsync(workItem);
    }
}

using MediatR;
using Samwise.Logic.WorkItems.Services;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;

namespace Samwise.Logic.WorkItems.Usecases;

public sealed record RestoreWorkItemUsecase(string WorkItemId, string Source = "Web") : IRequest;

internal sealed class RestoreWorkItemUsecaseHandler(
    IRepository<DomainWorkItem> repository,
    IWorkItemPlanningService planningService,
    IWorkItemEventPublisher eventPublisher,
    TimeProvider timeProvider) : IRequestHandler<RestoreWorkItemUsecase>
{
    public async Task Handle(RestoreWorkItemUsecase request, CancellationToken cancellationToken)
    {
        var workItem = await repository.GetRequiredByIdAsync(request.WorkItemId, cancellationToken);
        workItem.Restore(timeProvider.GetUtcNow(), request.Source);
        await repository.UpdateAsync(workItem, cancellationToken);
        await eventPublisher.PublishAsync(workItem);
        await planningService.PlanAsync(workItem.Id, cancellationToken);
    }
}

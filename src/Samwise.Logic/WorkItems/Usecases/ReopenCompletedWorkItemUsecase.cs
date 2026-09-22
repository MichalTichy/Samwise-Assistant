using MediatR;
using Samwise.Logic.WorkItems.Services;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;

namespace Samwise.Logic.WorkItems.Usecases;

public sealed record ReopenCompletedWorkItemUsecase(string WorkItemId, string Reason, string Source = "Web") : IRequest;

internal sealed class ReopenCompletedWorkItemUsecaseHandler(
    IRepository<DomainWorkItem> repository,
    IWorkItemPlanningService planningService,
    IWorkItemEventPublisher eventPublisher,
    TimeProvider timeProvider) : IRequestHandler<ReopenCompletedWorkItemUsecase>
{
    public async Task Handle(ReopenCompletedWorkItemUsecase request, CancellationToken cancellationToken)
    {
        var workItem = await repository.GetRequiredByIdAsync(request.WorkItemId, cancellationToken);
        workItem.ReopenCompleted(request.Reason, timeProvider.GetUtcNow(), request.Source);
        await repository.UpdateAsync(workItem, cancellationToken);
        await eventPublisher.PublishAsync(workItem);
        await planningService.PlanAsync(workItem.Id, cancellationToken);
    }
}

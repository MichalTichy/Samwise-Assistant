using MediatR;
using Samwise.Logic.WorkItems.Services;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;

namespace Samwise.Logic.WorkItems.Usecases;

public sealed record RetryWorkItemPlanningUsecase(string WorkItemId) : IRequest;

internal sealed class RetryWorkItemPlanningUsecaseHandler(
    IRepository<DomainWorkItem> repository,
    IWorkItemPlanningService planningService,
    TimeProvider timeProvider) : IRequestHandler<RetryWorkItemPlanningUsecase>
{
    public async Task Handle(RetryWorkItemPlanningUsecase request, CancellationToken cancellationToken)
    {
        var workItem = await repository.GetRequiredByIdAsync(request.WorkItemId, cancellationToken);
        workItem.RetryPlanning(timeProvider.GetUtcNow());
        await repository.UpdateAsync(workItem, cancellationToken);
        await planningService.PlanAsync(workItem.Id, cancellationToken);
    }
}

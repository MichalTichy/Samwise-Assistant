using MediatR;
using Samwise.Logic.WorkItems.Services;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;

namespace Samwise.Logic.WorkItems.Usecases;

public sealed record ReplanWorkItemUsecase(string WorkItemId) : IRequest;

internal sealed class ReplanWorkItemUsecaseHandler(
    IRepository<DomainWorkItem> repository,
    IWorkItemPlanningService planningService,
    TimeProvider timeProvider) : IRequestHandler<ReplanWorkItemUsecase>
{
    public async Task Handle(ReplanWorkItemUsecase request, CancellationToken cancellationToken)
    {
        var workItem = await repository.GetRequiredByIdAsync(request.WorkItemId, cancellationToken);
        workItem.Replan(timeProvider.GetUtcNow());
        await repository.UpdateAsync(workItem, cancellationToken);
        await planningService.PlanAsync(workItem.Id, cancellationToken);
    }
}

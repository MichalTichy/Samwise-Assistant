using MediatR;
using Samwise.Domain.WorkItems;
using Samwise.Logic.WorkItems.Services;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;
using Task = System.Threading.Tasks.Task;

namespace Samwise.Logic.WorkItems.Usecases;

public sealed record RejectWorkItemPlanUsecase(
    string WorkItemId,
    string PlanId,
    int WorkItemRevision,
    string Feedback) : IRequest;

internal sealed class RejectWorkItemPlanUsecaseHandler(
    IRepository<DomainWorkItem> workItemRepository,
    IWorkItemPlanningService planningService,
    TimeProvider timeProvider) : IRequestHandler<RejectWorkItemPlanUsecase>
{
    public async Task Handle(RejectWorkItemPlanUsecase request, CancellationToken cancellationToken)
    {
        var workItem = await workItemRepository.GetRequiredByIdAsync(request.WorkItemId, cancellationToken);
        workItem.Reject(request.PlanId, request.WorkItemRevision, request.Feedback, timeProvider.GetUtcNow());
        await workItemRepository.UpdateAsync(workItem, cancellationToken);
        await planningService.PlanAsync(workItem.Id, cancellationToken);
    }
}

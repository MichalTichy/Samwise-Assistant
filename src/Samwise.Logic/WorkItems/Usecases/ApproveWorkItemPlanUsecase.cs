using MediatR;
using Samwise.Domain.WorkItems;
using Samwise.Logic.WorkItems.Services;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;
using Task = System.Threading.Tasks.Task;

namespace Samwise.Logic.WorkItems.Usecases;

public sealed record ApproveWorkItemPlanUsecase(string WorkItemId, string PlanId, int WorkItemRevision) : IRequest;

internal sealed class ApproveWorkItemPlanUsecaseHandler(
    IRepository<DomainWorkItem> workItemRepository,
    IWorkItemExecutionService executionService,
    TimeProvider timeProvider) : IRequestHandler<ApproveWorkItemPlanUsecase>
{
    public async Task Handle(ApproveWorkItemPlanUsecase request, CancellationToken cancellationToken)
    {
        var workItem = await workItemRepository.GetRequiredByIdAsync(request.WorkItemId, cancellationToken);
        workItem.Approve(request.PlanId, request.WorkItemRevision, timeProvider.GetUtcNow());
        await workItemRepository.UpdateAsync(workItem, cancellationToken);
        await executionService.ExecuteAsync(
            new WorkItemExecutionRequest(
                workItem.Id,
                request.PlanId,
                request.WorkItemRevision),
            cancellationToken);
    }
}

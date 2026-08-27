using MediatR;
using Samwise.Domain.WorkItems;
using Samwise.Logic.WorkItems.Services;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;
using Task = System.Threading.Tasks.Task;

namespace Samwise.Logic.WorkItems.Usecases;

public sealed record AddWorkItemContextUsecase(string WorkItemId, string Text) : IRequest;

internal sealed class AddWorkItemContextUsecaseHandler(
    IRepository<DomainWorkItem> workItemRepository,
    IWorkItemPlanningService planningService,
    TimeProvider timeProvider) : IRequestHandler<AddWorkItemContextUsecase>
{
    public async Task Handle(AddWorkItemContextUsecase request, CancellationToken cancellationToken)
    {
        var workItem = await workItemRepository.GetRequiredByIdAsync(request.WorkItemId, cancellationToken);
        workItem.AddContext(request.Text, timeProvider.GetUtcNow());
        await workItemRepository.UpdateAsync(workItem, cancellationToken);
        await planningService.PlanAsync(workItem.Id, cancellationToken);
    }
}

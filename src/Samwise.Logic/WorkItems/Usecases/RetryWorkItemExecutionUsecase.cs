using MediatR;
using Samwise.Logic.WorkItems.Services;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;

namespace Samwise.Logic.WorkItems.Usecases;

public sealed record RetryWorkItemExecutionUsecase(string WorkItemId) : IRequest;

internal sealed class RetryWorkItemExecutionUsecaseHandler(
    IRepository<DomainWorkItem> repository,
    IWorkItemExecutionService executionService,
    IWorkItemEventPublisher eventPublisher,
    TimeProvider timeProvider) : IRequestHandler<RetryWorkItemExecutionUsecase>
{
    public async Task Handle(
        RetryWorkItemExecutionUsecase request,
        CancellationToken cancellationToken)
    {
        var workItem = await repository.GetRequiredByIdAsync(request.WorkItemId, cancellationToken);
        workItem.PrepareExecutionRetry(timeProvider.GetUtcNow());
        var plan = workItem.FindCurrentPlan()
            ?? throw new InvalidOperationException("The approved plan for this execution could not be found.");
        await repository.UpdateAsync(workItem, cancellationToken);
        await eventPublisher.PublishAsync(workItem);
        await executionService.ExecuteAsync(
            new WorkItemExecutionRequest(workItem.Id, plan.Id, workItem.Revision),
            cancellationToken);
    }
}

using MediatR;
using Shared.Infrastructure.Persistence.Repositories;
using DomainTask = Samwise.Domain.Tasks.Task;

namespace Samwise.Logic.Tasks.Usecases;

public sealed record RetryTaskPlanningUsecase(string TaskId) : IRequest;

internal sealed class RetryTaskPlanningUsecaseHandler(
    IRepository<DomainTask> repository,
    ISender sender,
    TimeProvider timeProvider) : IRequestHandler<RetryTaskPlanningUsecase>
{
    public async Task Handle(RetryTaskPlanningUsecase request, CancellationToken cancellationToken)
    {
        var task = await repository.GetRequiredByIdAsync(request.TaskId, cancellationToken);
        task.RetryPlanning(timeProvider.GetUtcNow());
        await repository.UpdateAsync(task, cancellationToken);
        await sender.Send(new PlanTaskUsecase(task.Id), cancellationToken);
    }
}

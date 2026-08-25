using MediatR;
using Samwise.Domain.Tasks;
using Shared.Infrastructure.Persistence.Repositories;
using DomainTask = Samwise.Domain.Tasks.Task;
using Task = System.Threading.Tasks.Task;

namespace Samwise.Logic.Tasks.Usecases;

public sealed record ApproveTaskPlanUsecase(string TaskId, string PlanId, int TaskRevision) : IRequest;

internal sealed class ApproveTaskPlanUsecaseHandler(
    IRepository<DomainTask> taskRepository,
    ISender sender,
    TimeProvider timeProvider) : IRequestHandler<ApproveTaskPlanUsecase>
{
    public async Task Handle(ApproveTaskPlanUsecase request, CancellationToken cancellationToken)
    {
        var task = await taskRepository.GetRequiredByIdAsync(request.TaskId, cancellationToken);
        task.Approve(request.PlanId, request.TaskRevision, timeProvider.GetUtcNow());
        await taskRepository.UpdateAsync(task, cancellationToken);
        await sender.Send(new ExecuteTaskUsecase(task.Id, request.PlanId, request.TaskRevision), cancellationToken);
    }
}

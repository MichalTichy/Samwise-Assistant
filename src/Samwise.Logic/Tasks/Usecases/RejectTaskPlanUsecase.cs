using MediatR;
using Samwise.Domain.Tasks;
using Shared.Infrastructure.Persistence.Repositories;
using DomainTask = Samwise.Domain.Tasks.Task;
using Task = System.Threading.Tasks.Task;

namespace Samwise.Logic.Tasks.Usecases;

public sealed record RejectTaskPlanUsecase(
    string TaskId,
    string PlanId,
    int TaskRevision,
    string Feedback) : IRequest;

internal sealed class RejectTaskPlanUsecaseHandler(
    IRepository<DomainTask> taskRepository,
    ISender sender,
    TimeProvider timeProvider) : IRequestHandler<RejectTaskPlanUsecase>
{
    public async Task Handle(RejectTaskPlanUsecase request, CancellationToken cancellationToken)
    {
        var task = await taskRepository.GetRequiredByIdAsync(request.TaskId, cancellationToken);
        task.Reject(request.PlanId, request.TaskRevision, request.Feedback, timeProvider.GetUtcNow());
        await taskRepository.UpdateAsync(task, cancellationToken);
        await sender.Send(new PlanTaskUsecase(task.Id), cancellationToken);
    }
}

using MediatR;

namespace Samwise.Logic.Tasks.Usecases;

public sealed record ApproveTaskPlanUsecase(Guid TaskId, Guid PlanId, int TaskRevision) : IRequest;

internal sealed class ApproveTaskPlanUsecaseHandler(ITaskStore store)
    : IRequestHandler<ApproveTaskPlanUsecase>
{
    public Task Handle(ApproveTaskPlanUsecase request, CancellationToken cancellationToken) =>
        store.ApprovePlanAsync(request.TaskId, request.PlanId, request.TaskRevision, cancellationToken);
}

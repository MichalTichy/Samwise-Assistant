using MediatR;

namespace Samwise.Logic.Tasks.Usecases;

public sealed record RejectTaskPlanUsecase(
    Guid TaskId,
    Guid PlanId,
    int TaskRevision,
    string Feedback) : IRequest;

internal sealed class RejectTaskPlanUsecaseHandler(ITaskStore store)
    : IRequestHandler<RejectTaskPlanUsecase>
{
    public Task Handle(RejectTaskPlanUsecase request, CancellationToken cancellationToken) =>
        store.RejectPlanAsync(
            request.TaskId,
            request.PlanId,
            request.TaskRevision,
            request.Feedback,
            cancellationToken);
}

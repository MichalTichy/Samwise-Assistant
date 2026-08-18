using MediatR;

namespace Samwise.Logic.Tasks.Usecases;

public sealed record RetryTaskPlanningUsecase(Guid TaskId) : IRequest;

internal sealed class RetryTaskPlanningUsecaseHandler(ITaskStore store)
    : IRequestHandler<RetryTaskPlanningUsecase>
{
    public Task Handle(RetryTaskPlanningUsecase request, CancellationToken cancellationToken) =>
        store.RetryPlanningAsync(request.TaskId, cancellationToken);
}

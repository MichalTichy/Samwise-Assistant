using MediatR;

namespace Samwise.Logic.Tasks.Usecases;

public sealed record GetTaskDetailsUsecase(Guid TaskId) : IRequest<TaskDetails?>;

internal sealed class GetTaskDetailsUsecaseHandler(ITaskStore store)
    : IRequestHandler<GetTaskDetailsUsecase, TaskDetails?>
{
    public Task<TaskDetails?> Handle(GetTaskDetailsUsecase request, CancellationToken cancellationToken) =>
        store.GetTaskAsync(request.TaskId, cancellationToken);
}

using MediatR;

namespace Samwise.Logic.Tasks.Usecases;

public sealed record DiscardTaskUsecase(Guid TaskId) : IRequest;

internal sealed class DiscardTaskUsecaseHandler(ITaskStore store)
    : IRequestHandler<DiscardTaskUsecase>
{
    public Task Handle(DiscardTaskUsecase request, CancellationToken cancellationToken) =>
        store.DiscardTaskAsync(request.TaskId, cancellationToken);
}

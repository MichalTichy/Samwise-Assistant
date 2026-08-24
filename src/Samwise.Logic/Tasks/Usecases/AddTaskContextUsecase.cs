using MediatR;

namespace Samwise.Logic.Tasks.Usecases;

public sealed record AddTaskContextUsecase(Guid TaskId, string Text) : IRequest;

internal sealed class AddTaskContextUsecaseHandler(ITaskStore store)
    : IRequestHandler<AddTaskContextUsecase>
{
    public Task Handle(AddTaskContextUsecase request, CancellationToken cancellationToken) =>
        store.AddContextAsync(request.TaskId, request.Text, cancellationToken);
}

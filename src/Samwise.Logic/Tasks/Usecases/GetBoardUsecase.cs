using MediatR;

namespace Samwise.Logic.Tasks.Usecases;

public sealed record GetBoardUsecase(bool IncludeArchived = false) : IRequest<IReadOnlyList<TaskCard>>;

internal sealed class GetBoardUsecaseHandler(ITaskStore store)
    : IRequestHandler<GetBoardUsecase, IReadOnlyList<TaskCard>>
{
    public Task<IReadOnlyList<TaskCard>> Handle(GetBoardUsecase request, CancellationToken cancellationToken) =>
        store.GetBoardAsync(request.IncludeArchived, cancellationToken);
}

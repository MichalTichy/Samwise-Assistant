using MediatR;

namespace Samwise.Logic.Tasks.Usecases;

public sealed record GetInboxUsecase : IRequest<IReadOnlyList<InboxEventItem>>;

internal sealed class GetInboxUsecaseHandler(ITaskStore store)
    : IRequestHandler<GetInboxUsecase, IReadOnlyList<InboxEventItem>>
{
    public Task<IReadOnlyList<InboxEventItem>> Handle(GetInboxUsecase request, CancellationToken cancellationToken) =>
        store.GetInboxAsync(cancellationToken);
}

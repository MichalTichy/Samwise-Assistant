using MediatR;
using Samwise.Domain.Inbox;
using Samwise.Logic.Inbox.Specifications;
using Samwise.Logic.Tasks;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Inbox.Usecases;

public sealed record GetInboxUsecase : IRequest<IReadOnlyList<InboxEventItem>>;

internal sealed class GetInboxUsecaseHandler(IReadOnlyRepository<InboundEvent> repository)
    : IRequestHandler<GetInboxUsecase, IReadOnlyList<InboxEventItem>>
{
    public async Task<IReadOnlyList<InboxEventItem>> Handle(
        GetInboxUsecase request,
        CancellationToken cancellationToken)
    {
        var events = await repository.ListAsync(new InboxEventsSpecification(), cancellationToken);
        return events.Select(item => new InboxEventItem(
            item.Id,
            item.Source,
            item.Text,
            item.ReceivedAt,
            item.Handling,
            item.TaskId,
            item.Explanation)).ToList();
    }
}

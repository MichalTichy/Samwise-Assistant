namespace Samwise.Logic.Inbox.Services;

internal interface IInboundEventClassificationService
{
    Task<IReadOnlyList<string>> ClassifyAsync(string inboundEventId, CancellationToken cancellationToken);
}

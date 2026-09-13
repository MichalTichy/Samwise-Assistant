namespace Samwise.Logic.Inbox.Services;

internal interface IInboundEventClassificationService
{
    Task<string?> ClassifyAsync(string inboundEventId, CancellationToken cancellationToken);
}

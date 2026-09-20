using Samwise.Inputs.Teams.Discovery;

namespace Samwise.Inputs.Teams.Ingestion;

public interface ITeamsInputReader
{
    Task<TeamsInputCatalog> GetCatalogAsync(CancellationToken cancellationToken = default);

    Task<TeamsInputReadResult> ReadNewAsync(
        DateTimeOffset since,
        DateTimeOffset until,
        bool includeChatMessages,
        bool includeChannelMessages,
        bool includeCallTranscripts,
        CancellationToken cancellationToken = default);
}

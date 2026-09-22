using System.Text.Json;

namespace Samwise.Infrastructure.Teams.Graph;

internal interface ITeamsGraphClient
{
    Task<JsonElement> GetDelegatedAsync(string path, CancellationToken cancellationToken);
    Task<IReadOnlyList<JsonElement>> GetDelegatedCollectionAsync(string path, CancellationToken cancellationToken);
    Task<IReadOnlyList<JsonElement>> GetApplicationCollectionAsync(string path, CancellationToken cancellationToken);
}

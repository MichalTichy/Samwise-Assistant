using System.Text.Json;

namespace Samwise.Infrastructure.Teams.Readers;

internal sealed record TeamsTranscriptDiscoveryResult(
    IReadOnlyList<JsonElement> Transcripts,
    IReadOnlyList<string> Errors);

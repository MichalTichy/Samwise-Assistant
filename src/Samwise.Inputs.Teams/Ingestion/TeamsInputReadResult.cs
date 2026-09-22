using Samwise.Inputs.Teams.Ingestion.Activities;

namespace Samwise.Inputs.Teams.Ingestion;

public sealed record TeamsInputReadResult(
    IReadOnlyList<TeamsActivitySnapshot> Activities,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> SkippedMeetings);

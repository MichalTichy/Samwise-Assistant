namespace Samwise.Inputs.Teams;

public sealed record TeamsInputReadResult(
    IReadOnlyList<TeamsActivitySnapshot> Activities,
    IReadOnlyList<string> Errors);

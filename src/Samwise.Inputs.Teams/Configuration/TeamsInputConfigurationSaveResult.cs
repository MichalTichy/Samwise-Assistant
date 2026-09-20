namespace Samwise.Inputs.Teams.Configuration;

public sealed record TeamsInputConfigurationSaveResult(bool Succeeded, IReadOnlyList<string> Errors);

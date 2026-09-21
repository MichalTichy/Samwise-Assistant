namespace Samwise.Inputs.Todoist.Configuration;

public sealed record TodoistInputConfigurationSaveResult(bool Succeeded, IReadOnlyList<string> Errors);

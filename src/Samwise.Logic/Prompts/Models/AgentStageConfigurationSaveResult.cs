namespace Samwise.Logic.Prompts;

public sealed record AgentStageConfigurationSaveResult(bool Succeeded, IReadOnlyList<string> Errors)
{
    public static AgentStageConfigurationSaveResult Success { get; } = new(true, []);
}

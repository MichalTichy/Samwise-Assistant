using Shared.Infrastructure.Persistence;

namespace Samwise.Domain.Agents;

public enum AgentStage
{
    Intake,
    Planning,
    Execution
}

public sealed class AgentConfiguration : IItemWithId
{
    public const string DocumentId = "default";

    public string Id { get; set; } = DocumentId;
    public AgentStageConfiguration Intake { get; set; } = new();
    public AgentStageConfiguration Planning { get; set; } = new();
    public AgentStageConfiguration Execution { get; set; } = new();

    public AgentStageConfiguration GetStage(AgentStage stage) => stage switch
    {
        AgentStage.Intake => Intake,
        AgentStage.Planning => Planning,
        AgentStage.Execution => Execution,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unsupported agent stage.")
    };

    public void UpdateStage(
        AgentStage stage,
        string prompt,
        string model,
        string reasoningEffort,
        DateTimeOffset updatedAt)
    {
        var configuration = GetStage(stage);
        configuration.Prompt = prompt;
        configuration.Model = model;
        configuration.ReasoningEffort = reasoningEffort;
        configuration.UpdatedAt = updatedAt;
    }
}

public sealed class AgentStageConfiguration
{
    public string Prompt { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string ReasoningEffort { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

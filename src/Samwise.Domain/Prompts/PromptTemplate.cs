namespace Samwise.Domain.Prompts;

public enum PromptStage
{
    Intake,
    Planning,
    Execution
}

public sealed class PromptTemplate
{
    public string Id { get; set; } = string.Empty;
    public PromptStage Stage { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

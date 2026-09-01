using Samwise.Domain.Agents;

namespace Samwise.Logic.Prompts;

public interface IPromptRenderer
{
    IReadOnlyList<PromptPlaceholder> GetPlaceholders(AgentStage stage);

    IReadOnlyList<string> Validate(AgentStage stage, string template);

    PromptRenderResult Render(
        AgentStage stage,
        string template,
        IReadOnlyDictionary<string, string?> values);
}

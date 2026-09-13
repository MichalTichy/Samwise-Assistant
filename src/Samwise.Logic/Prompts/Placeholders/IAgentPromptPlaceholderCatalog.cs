using Samwise.Domain.Agents;

namespace Samwise.Logic.Prompts;

public interface IAgentPromptPlaceholderCatalog
{
    IReadOnlyList<PromptPlaceholder> GetForStage(AgentStage stage);
}

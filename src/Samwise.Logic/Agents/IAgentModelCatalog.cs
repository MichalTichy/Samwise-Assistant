using Samwise.Domain.Agents;

namespace Samwise.Logic.Agents;

public sealed record AgentModelDefinition(
    string Id,
    IReadOnlyList<string> SupportedReasoningEfforts,
    string DefaultReasoningEffort);

public interface IAgentModelCatalog
{
    Task<IReadOnlyList<AgentModelDefinition>> GetModelsAsync(
        AgentStage stage,
        CancellationToken cancellationToken = default);
}

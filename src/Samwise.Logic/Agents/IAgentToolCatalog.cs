using Samwise.Domain.Agents;

namespace Samwise.Logic.Agents;

public interface IAgentToolCatalog
{
    Task<ToolCatalogSnapshot> GetToolsAsync(
        AgentStage stage,
        CancellationToken cancellationToken = default);
}

namespace Samwise.Logic.Agents;

public interface IAgentToolCatalog
{
    Task<ToolCatalogSnapshot> GetPlanningToolsAsync(CancellationToken cancellationToken = default);

    Task<ToolCatalogSnapshot> GetExecutionToolsAsync(CancellationToken cancellationToken = default);
}

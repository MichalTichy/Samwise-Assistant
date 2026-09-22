using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Samwise.Domain.Agents;
using Samwise.Infrastructure.Mcp.Serialization;
using Samwise.Logic.Agents;

namespace Samwise.Infrastructure.Mcp.Tools;

[McpServerToolType]
public sealed class ExecutionToolCatalogTools(IAgentToolCatalog catalog)
{
    [McpServerTool(Name = "get_execution_tool_schema")]
    [Description("Retrieves the current input schema for one execution tool by its exact qualified ID. Use this before proposing its arguments in a plan.")]
    public async Task<string> GetExecutionToolSchemaAsync(
        [Description("Exact qualified tool ID from the execution capability catalogue.")] string toolId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await catalog.GetToolsAsync(AgentStage.Execution, cancellationToken);
        var tool = snapshot.Tools.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, toolId, StringComparison.Ordinal) && candidate.CanInvoke);
        if (tool is null)
        {
            return SamwiseMcpJson.Serialize(new { found = false, catalog_version = snapshot.CatalogVersion });
        }

        using var schema = JsonDocument.Parse(tool.InputSchemaJson);
        return SamwiseMcpJson.Serialize(new
        {
            found = true,
            catalog_version = snapshot.CatalogVersion,
            tool = new
            {
                id = tool.Id,
                name = tool.Name,
                description = tool.Description,
                input_schema = schema.RootElement
            }
        });
    }
}

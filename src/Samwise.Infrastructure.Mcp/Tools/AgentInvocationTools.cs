using System.ComponentModel;
using ModelContextProtocol.Server;
using Samwise.Domain.Agents;
using Samwise.Infrastructure.Mcp.Serialization;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Infrastructure.Mcp.Tools;

[McpServerToolType]
public sealed class AgentInvocationTools(IReadOnlyRepository<AgentInvocation> repository)
{
    [McpServerTool(Name = "get_agent_invocation")]
    [Description("Gets the complete recorded Samwise agent invocation, including the exact rendered prompt, raw response, error, model settings, and available-tool snapshot.")]
    public async Task<string> GetAgentInvocationAsync(
        [Description("The agent invocation ID stored on a Work Item plan or completion, or shown in Agent activity.")]
        string agentInvocationId,
        CancellationToken cancellationToken = default)
    {
        var invocation = await repository.GetByIdAsync(agentInvocationId, cancellationToken);
        return invocation is null
            ? SamwiseMcpJson.Serialize(new { error = $"Agent invocation '{agentInvocationId}' was not found." })
            : SamwiseMcpJson.SerializeRuntime(invocation);
    }
}

using System.ComponentModel;
using ModelContextProtocol.Server;
using Samwise.Domain.Agents;
using Samwise.Infrastructure.Mcp.Serialization;
using Samwise.Logic.WorkItems.Specifications;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Infrastructure.Mcp.Tools;

[McpServerToolType]
public sealed class AgentInvocationTools(IReadOnlyRepository<AgentInvocation> repository)
{
    [McpServerTool(Name = "list_agent_invocations")]
    [Description("Lists recent agent invocations for a Samwise Work Item, including failures. Use get_agent_invocation with a returned ID for the complete prompt, response, error, model settings, and tool snapshot.")]
    public async Task<string> ListAgentInvocationsAsync(
        [Description("The Work Item ID returned by list_work_items.")]
        string workItemId,
        [Description("Maximum number of invocations to return, from 1 to 100.")]
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var boundedLimit = Math.Clamp(limit, 1, 100);
        var invocations = await repository.ListAsync(
            new WorkItemInvocationsSpecification(workItemId, boundedLimit),
            cancellationToken);
        return SamwiseMcpJson.Serialize(new
        {
            count = invocations.Count,
            invocations = invocations.Select(invocation => new
            {
                invocation.Id,
                invocation.WorkItemId,
                invocation.WorkItemRevision,
                invocation.Profile,
                invocation.Provider,
                invocation.Model,
                invocation.ReasoningEffort,
                invocation.Status,
                errorPreview = invocation.Error is null
                    ? null
                    : SamwiseMcpJson.Truncate(invocation.Error, 500),
                invocation.StartedAt,
                invocation.FinishedAt
            })
        });
    }

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

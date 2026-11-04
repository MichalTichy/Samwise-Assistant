using System.ComponentModel;
using MediatR;
using ModelContextProtocol.Server;
using Samwise.Infrastructure.Mcp.Serialization;
using Samwise.Logic.WorkItems.Usecases;

namespace Samwise.Infrastructure.Mcp.Tools;

[McpServerToolType]
public sealed class WorkItemActionTools(ISender sender)
{
    [McpServerTool(Name = "replan_work_item")]
    [Description("Invalidates the current unexecuted plan or pending planning question and immediately generates a new plan from the Work Item's current data. Previous plans and agent activity remain in history.")]
    public async Task<string> ReplanWorkItemAsync(
        [Description("The Work Item ID returned by list_work_items. It must be awaiting user input, an external event, approval, or in NeedsAttention.")]
        string workItemId,
        CancellationToken cancellationToken = default)
    {
        await sender.Send(new ReplanWorkItemUsecase(workItemId), cancellationToken);
        return SamwiseMcpJson.Serialize(new
        {
            workItemId,
            replanCompleted = true
        });
    }

    [McpServerTool(Name = "restore_work_item")]
    [Description("Restores a discarded Work Item and immediately generates a new plan. Existing plans and discard history remain available for audit.")]
    public async Task<string> RestoreWorkItemAsync(
        [Description("The discarded Work Item ID returned by list_work_items.")]
        string workItemId,
        CancellationToken cancellationToken = default)
    {
        await sender.Send(new RestoreWorkItemUsecase(workItemId, "MCP"), cancellationToken);
        return SamwiseMcpJson.Serialize(new
        {
            workItemId,
            restoreCompleted = true
        });
    }

    [McpServerTool(Name = "reopen_completed_work_item")]
    [Description("Reopens a completed Work Item whose real-world outcome remains unfinished, preserves its previous completion in the history, and generates a new plan. Does not repeat the previous execution automatically.")]
    public async Task<string> ReopenCompletedWorkItemAsync(
        [Description("The completed Work Item ID returned by list_work_items.")]
        string workItemId,
        [Description("Why the real-world outcome is still unfinished. This becomes planning context.")]
        string reason,
        CancellationToken cancellationToken = default)
    {
        await sender.Send(new ReopenCompletedWorkItemUsecase(workItemId, reason, "MCP"), cancellationToken);
        return SamwiseMcpJson.Serialize(new
        {
            workItemId,
            reopened = true
        });
    }
}

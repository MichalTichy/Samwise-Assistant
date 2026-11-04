using System.ComponentModel;
using ModelContextProtocol.Server;
using Samwise.Domain.WorkItems;
using Samwise.Infrastructure.Mcp.Serialization;
using Samwise.Logic.WorkItems.Specifications;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;

namespace Samwise.Infrastructure.Mcp.Tools;

[McpServerToolType]
public sealed class WorkItemTools(IReadOnlyRepository<DomainWorkItem> repository)
{
    [McpServerTool(Name = "list_work_items")]
    [Description("Lists recent Samwise Work Items, optionally filtered by status. Use get_work_item for the complete item and plan history.")]
    public async Task<string> ListWorkItemsAsync(
        [Description("Optional status: Planning, AwaitingUserInput, AwaitingExternalEvent, AwaitingApproval, Approved, Executing, NeedsAttention, Completed, Discarded, or OutcomeUnknown.")]
        string? status = null,
        [Description("Maximum number of Work Items to return, from 1 to 100.")]
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        WorkItemStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<WorkItemStatus>(status, true, out var statusValue))
            {
                return SamwiseMcpJson.Serialize(new
                {
                    error = $"Unknown Work Item status '{status}'.",
                    allowedStatuses = Enum.GetNames<WorkItemStatus>()
                });
            }

            parsedStatus = statusValue;
        }

        var boundedLimit = Math.Clamp(limit, 1, 100);
        var workItems = await repository.ListAsync(
            new RecentWorkItemsSpecification(parsedStatus, boundedLimit),
            cancellationToken);
        return SamwiseMcpJson.Serialize(new
        {
            count = workItems.Count,
            workItems = workItems.Select(workItem => new
            {
                workItem.Id,
                workItem.Title,
                workItem.Status,
                workItem.Revision,
                descriptionPreview = SamwiseMcpJson.Truncate(workItem.Description, 500),
                workItem.CurrentPlanId,
                pendingQuestion = workItem.PendingPlanningQuestion?.Prompt,
                workItem.WaitingReason,
                planCount = workItem.Plans.Count,
                workItem.CreatedAt,
                workItem.UpdatedAt,
                workItem.CompletedAt,
                completionKind = workItem.Completion?.Kind,
                workItem.DiscardedAt,
                workItem.DiscardReason
            })
        });
    }

    [McpServerTool(Name = "get_work_item")]
    [Description("Gets a complete Samwise Work Item, including its description, pending planning question, decisions, plans, reviews, comments, and completion details.")]
    public async Task<string> GetWorkItemAsync(
        [Description("The Work Item ID returned by list_work_items.")]
        string workItemId,
        CancellationToken cancellationToken = default)
    {
        var workItem = await repository.GetByIdAsync(workItemId, cancellationToken);
        return workItem is null
            ? SamwiseMcpJson.Serialize(new { error = $"Work Item '{workItemId}' was not found." })
            : SamwiseMcpJson.SerializeRuntime(workItem);
    }
}

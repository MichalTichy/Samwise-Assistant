using System.ComponentModel;
using ModelContextProtocol.Server;
using Samwise.Domain.Inbox;
using Samwise.Infrastructure.Mcp.Serialization;
using Samwise.Logic.Inbox.Specifications;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Infrastructure.Mcp.Tools;

[McpServerToolType]
public sealed class InputEventTools(IReadOnlyRepository<InboundEvent> repository)
{
    [McpServerTool(Name = "list_input_events")]
    [Description("Lists recent inputs received by Samwise and how intake handled them. Use get_input_event for complete source content.")]
    public async Task<string> ListInputEventsAsync(
        [Description("Maximum number of input events to return, from 1 to 100.")]
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        var boundedLimit = Math.Clamp(limit, 1, 100);
        var events = await repository.ListAsync(
            new RecentInboxEventsSpecification(boundedLimit),
            cancellationToken);
        return SamwiseMcpJson.Serialize(new
        {
            count = events.Count,
            inputEvents = events.Select(inputEvent => new
            {
                inputEvent.Id,
                inputEvent.Source,
                inputEvent.ExternalId,
                inputEvent.ReceivedAt,
                inputEvent.HandledAt,
                inputEvent.Handling,
                inputEvent.WorkItemId,
                inputEvent.Explanation,
                inputPreview = SamwiseMcpJson.Truncate(inputEvent.GetIntakeText(), 500)
            })
        });
    }

    [McpServerTool(Name = "get_input_event")]
    [Description("Gets a complete Samwise input event, including its source-specific content and intake handling result.")]
    public async Task<string> GetInputEventAsync(
        [Description("The input event ID returned by list_input_events.")]
        string inputEventId,
        CancellationToken cancellationToken = default)
    {
        var inputEvent = await repository.GetByIdAsync(inputEventId, cancellationToken);
        return inputEvent is null
            ? SamwiseMcpJson.Serialize(new { error = $"Input event '{inputEventId}' was not found." })
            : SamwiseMcpJson.SerializeRuntime(inputEvent);
    }
}

using System.ComponentModel;
using MediatR;
using ModelContextProtocol.Server;
using Samwise.Infrastructure.Mcp.Serialization;
using Samwise.Logic.Inbox.Usecases;

namespace Samwise.Infrastructure.Mcp.Tools;

[McpServerToolType]
public sealed class InputEventActionTools(ISender sender)
{
    [McpServerTool(Name = "reprocess_ignored_input_event")]
    [Description("Runs intake classification again for an input event that was previously ignored. Use after correcting general intake guidance. Events that already changed Work Items cannot be reprocessed through this tool.")]
    public async Task<string> ReprocessIgnoredInputEventAsync(
        [Description("The ignored input event ID returned by list_input_events.")]
        string inputEventId,
        CancellationToken cancellationToken = default)
    {
        await sender.Send(new RetryInboundEventUsecase(inputEventId), cancellationToken);
        return SamwiseMcpJson.Serialize(new
        {
            inputEventId,
            reprocessingCompleted = true
        });
    }
}

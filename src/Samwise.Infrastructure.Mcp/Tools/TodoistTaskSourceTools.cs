using System.ComponentModel;
using MediatR;
using ModelContextProtocol.Server;
using Samwise.Infrastructure.Mcp.Serialization;
using Samwise.TaskSources.Todoist.Usecases.Dismissal;

namespace Samwise.Infrastructure.Mcp.Tools;

[McpServerToolType]
public sealed class TodoistTaskSourceTools(ISender sender)
{
    [McpServerTool(Name = "dismiss_todoist_task")]
    [Description("Owner action. Removes the Todoist task behind an imported Samwise task from Samwise, including every future occurrence and generation. The active Samwise task is retired and unstarted work is withdrawn; an executing or uncertain run is never interrupted. Todoist itself is not changed and history stays readable. Use only on the owner's explicit request; never as an outcome of Planning or Execution. Pass the Samwise agent task ID from get_agent_task.")]
    public async Task<string> DismissAsync(string agentTaskId, CancellationToken cancellationToken = default) =>
        SamwiseMcpJson.Serialize(await sender.Send(new DismissTodoistTaskUsecase(agentTaskId), cancellationToken));

    [McpServerTool(Name = "restore_todoist_task")]
    [Description("Owner action. Lifts a previous removal so the next Todoist sync imports the task again in the normal way. Pass the Todoist task ID (externalSource.taskId from get_agent_task).")]
    public async Task<string> RestoreAsync(string todoistTaskId, CancellationToken cancellationToken = default) =>
        SamwiseMcpJson.Serialize(await sender.Send(new RestoreTodoistTaskUsecase(todoistTaskId), cancellationToken));
}

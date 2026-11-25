using System.ComponentModel;
using MediatR;
using ModelContextProtocol.Server;
using Samwise.Domain.Tasks;
using Samwise.Infrastructure.Mcp.Serialization;
using Samwise.Logic.Tasks.Specifications;
using Samwise.Logic.Tasks.Usecases;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Infrastructure.Mcp.Tools;

[McpServerToolType]
public sealed class AgentTaskTools(IReadOnlyRepository<AgentTask> tasks,
    IReadOnlyRepository<AgentRoutine> routines, ISender sender)
{
    [McpServerTool(Name = "list_agent_tasks", ReadOnly = true)]
    [Description("Lists internal tasks for Samwise, including checklists and next check times. Human tasks belong in Todoist.")]
    public async Task<string> ListAsync(bool includeCompleted = false, CancellationToken cancellationToken = default) =>
        SamwiseMcpJson.Serialize(await tasks.ListAsync(new AgentTasksSpecification(includeCompleted), cancellationToken));

    [McpServerTool(Name = "get_agent_task", ReadOnly = true)]
    [Description("Reads the exact internal task, current version, checklist evidence and linked work item history. Read before updating.")]
    public async Task<string> GetAsync(string taskId, CancellationToken cancellationToken = default) =>
        SamwiseMcpJson.Serialize(await tasks.GetRequiredByIdAsync(taskId, cancellationToken));

    [McpServerTool(Name = "save_agent_task")]
    [Description("Owner task editor. Creates or replaces an internal task and withdraws any unexecuted pending plan. Never grant this tool to an Execution profile; agents must use save_agent_task_progress with their executing work item ID. Existing ID and version are required for updates; null ID and version 0 create. Preserve verified effects and provide a future check for unfinished tasks.")]
    public async Task<string> SaveAsync(string? taskId, int expectedVersion, string title, string instructions,
        TaskChecklistItem[] checklist, DateTimeOffset? nextRunAt, string? originWorkItemId = null,
        CancellationToken cancellationToken = default) => SamwiseMcpJson.Serialize(await sender.Send(
            new SaveAgentTaskUsecase(taskId, expectedVersion, title, instructions, checklist, nextRunAt, originWorkItemId,
                IsOwnerEdit: true),
            cancellationToken));

    [McpServerTool(Name = "save_agent_task_progress")]
    [Description("During approved Execution, creates a dated self-check or saves approved task progress/evidence and its next check. Requires the currently executing work item ID; the server verifies it and links new tasks to that origin. Supply current task ID/version for updates or null ID/version 0 for creation. Preserve checklist IDs and completed effects. Every completed item requires evidence; unfinished work requires a future next check before the intervention completes. This grants no authority for the later run.")]
    public async Task<string> SaveProgressAsync(string executingWorkItemId, string? taskId, int expectedVersion,
        string title, string instructions, TaskChecklistItem[] checklist, DateTimeOffset? nextRunAt,
        CancellationToken cancellationToken = default) => SamwiseMcpJson.Serialize(await sender.Send(
            new SaveAgentTaskUsecase(taskId, expectedVersion, title, instructions, checklist, nextRunAt,
                OriginWorkItemId: executingWorkItemId, IsOwnerEdit: false, ExecutingWorkItemId: executingWorkItemId),
            cancellationToken));

    [McpServerTool(Name = "list_agent_routines", ReadOnly = true)]
    [Description("Lists recurring task templates. Instances have independent checklists and context.")]
    public async Task<string> ListRoutinesAsync(CancellationToken cancellationToken = default) =>
        SamwiseMcpJson.Serialize(await routines.ListAsync(new AgentRoutinesSpecification(), cancellationToken));

    [McpServerTool(Name = "save_agent_routine")]
    [Description("Creates or updates a recurring template using a five-field Cron expression and explicit time zone. Existing instances are unchanged; only future generated instances use changes. Recurrence creation needs explicit owner delegation. This does not grant automatic approval or broaden permissions.")]
    public async Task<string> SaveRoutineAsync(string? routineId, int expectedVersion, string title,
        string instructions, TaskChecklistItem[] checklist, string cron, string timeZoneId, bool isEnabled,
        CancellationToken cancellationToken = default) => SamwiseMcpJson.Serialize(await sender.Send(
            new SaveAgentRoutineUsecase(routineId, expectedVersion, title, instructions, checklist, cron, timeZoneId, isEnabled),
            cancellationToken));
}

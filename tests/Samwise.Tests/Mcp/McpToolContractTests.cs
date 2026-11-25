using System.Reflection;
using ModelContextProtocol.Server;
using Samwise.Infrastructure.Mcp.Installers;
using Samwise.Infrastructure.Mcp.Tools;

namespace Samwise.Tests.Mcp;

public sealed class McpToolContractTests
{
    [Fact]
    public void ExecutionTaskToolRequiresWorkItemAndOwnerToolHasNoExecutionSwitch()
    {
        var progress = typeof(AgentTaskTools).GetMethod(nameof(AgentTaskTools.SaveProgressAsync))!;
        var workItem = Assert.Single(progress.GetParameters(), item => item.Name == "executingWorkItemId");
        Assert.False(workItem.HasDefaultValue);
        Assert.Equal(NullabilityState.NotNull, new NullabilityInfoContext().Create(workItem).ReadState);
        Assert.DoesNotContain(typeof(AgentTaskTools).GetMethod(nameof(AgentTaskTools.SaveAsync))!.GetParameters(),
            item => item.Name == "executingWorkItemId");
    }

    [Fact]
    public void SavePlaybookDoesNotExposeAutomaticApprovalEligibility()
    {
        var savePlaybook = typeof(PlaybookTools).GetMethod(nameof(PlaybookTools.SavePlaybookAsync));

        Assert.NotNull(savePlaybook);
        Assert.DoesNotContain(
            savePlaybook.GetParameters(),
            parameter => parameter.Name == "suitableForAutomaticApproval");
    }

    [Fact]
    public void McpAssemblyExposesTheExpectedNarrowToolSurface()
    {
        var toolNames = typeof(McpInstaller).Assembly
            .GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(type => type.GetMethods())
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
            .Where(attribute => attribute is not null)
            .Select(attribute => attribute!.Name
                ?? throw new InvalidOperationException("Every Samwise MCP tool must have an explicit name."))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "delete_playbook",
                "get_agent_invocation",
                "get_agent_settings",
                "get_agent_task",
                "get_execution_tool_schema",
                "get_input_event",
                "get_playbook",
                "get_work_item",
                "list_agent_invocations",
                "list_agent_routines",
                "list_agent_tasks",
                "list_input_events",
                "list_playbooks",
                "list_work_items",
                "reopen_completed_work_item",
                "replan_work_item",
                "reprocess_ignored_input_event",
                "restore_work_item",
                "save_agent_prompt",
                "save_agent_routine",
                "save_agent_task",
                "save_agent_task_progress",
                "save_playbook"
            ],
            toolNames);
    }
}

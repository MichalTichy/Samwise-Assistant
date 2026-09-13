using System.Reflection;
using ModelContextProtocol.Server;
using Samwise.Infrastructure.Mcp.Installers;

namespace Samwise.Tests.Mcp;

public sealed class McpToolContractTests
{
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
                "get_agent_settings",
                "get_input_event",
                "get_work_item",
                "list_input_events",
                "list_playbooks",
                "list_work_items",
                "save_agent_prompt",
                "save_playbook"
            ],
            toolNames);
    }
}

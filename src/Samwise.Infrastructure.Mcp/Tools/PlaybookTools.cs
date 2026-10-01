using System.ComponentModel;
using MediatR;
using ModelContextProtocol.Server;
using Samwise.Domain.Playbooks;
using Samwise.Infrastructure.Mcp.Serialization;
using Samwise.Logic.Playbooks.Specifications;
using Samwise.Logic.Playbooks.Usecases;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Infrastructure.Mcp.Tools;

[McpServerToolType]
public sealed class PlaybookTools(
    IReadOnlyRepository<Playbook> repository,
    ISender sender)
{
    [McpServerTool(Name = "list_playbooks")]
    [Description("Lists all Samwise Playbooks, including disabled Playbooks and their complete guidance.")]
    public async Task<string> ListPlaybooksAsync(CancellationToken cancellationToken = default)
    {
        var playbooks = await repository.ListAsync(
            new PlaybooksSpecification(),
            cancellationToken);
        return SamwiseMcpJson.Serialize(new
        {
            count = playbooks.Count,
            playbooks
        });
    }

    [McpServerTool(Name = "get_playbook")]
    [Description("Retrieves the complete guidance for one enabled Samwise Playbook by its exact ID.")]
    public async Task<string> GetPlaybookAsync(
        [Description("Exact Playbook ID from the planning prompt catalogue.")] string playbookId,
        CancellationToken cancellationToken = default)
    {
        var playbook = await repository.GetByIdAsync(playbookId, cancellationToken);
        if (playbook is not { IsEnabled: true })
        {
            return SamwiseMcpJson.Serialize(new { found = false });
        }

        return SamwiseMcpJson.Serialize(new { found = true, playbook });
    }

    [McpServerTool(Name = "save_playbook")]
    [Description("Creates or updates a Samwise Playbook. Supply an existing playbook_id to update it; omit the ID to create one.")]
    public async Task<string> SavePlaybookAsync(
        [Description("Short Playbook name, up to 80 characters.")]
        string name,
        [Description("Concise guidance describing when this Playbook applies, up to 1,000 characters.")]
        string applicability,
        [Description("Complete planning guidance for handling the scenario.")]
        string description,
        [Description("Whether this Playbook is supplied to the agents.")]
        bool isEnabled,
        [Description("Existing Playbook ID to update, or omit to create a new Playbook.")]
        string? playbookId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(
            new SavePlaybookUsecase(playbookId, name, applicability, description, isEnabled),
            cancellationToken);
        return SamwiseMcpJson.Serialize(result);
    }
}

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

    [McpServerTool(Name = "save_playbook")]
    [Description("Creates or updates a Samwise Playbook. Supply an existing playbook_id to update it; omit the ID to create one.")]
    public async Task<string> SavePlaybookAsync(
        [Description("Short Playbook name, up to 80 characters.")]
        string name,
        [Description("Complete steering guidance for intake and planning.")]
        string description,
        [Description("Whether this Playbook is supplied to the agents.")]
        bool isEnabled,
        [Description("Existing Playbook ID to update, or omit to create a new Playbook.")]
        string? playbookId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(
            new SavePlaybookUsecase(playbookId, name, description, isEnabled),
            cancellationToken);
        return SamwiseMcpJson.Serialize(result);
    }
}

using System.ComponentModel;
using MediatR;
using ModelContextProtocol.Server;
using Samwise.Domain.Agents;
using Samwise.Infrastructure.Mcp.Serialization;
using Samwise.Logic.Prompts;
using Samwise.Logic.Prompts.Usecases;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Infrastructure.Mcp.Tools;

[McpServerToolType]
public sealed class AgentSettingsTools(
    IReadOnlyRepository<AgentConfiguration> repository,
    IAgentPromptPlaceholderCatalog placeholderCatalog,
    ISender sender)
{
    [McpServerTool(Name = "get_agent_settings")]
    [Description("Gets the current intake, planning, and execution prompts together with their configured models and reasoning efforts. Secrets are not part of agent settings and are never returned.")]
    public async Task<string> GetAgentSettingsAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await repository.GetByIdAsync(
            AgentConfiguration.DocumentId,
            cancellationToken);
        if (configuration is null)
        {
            return SamwiseMcpJson.Serialize(new { error = "Agent settings have not been initialized." });
        }

        return SamwiseMcpJson.Serialize(new
        {
            configuration.Id,
            stages = Enum.GetValues<AgentStage>().Select(stage =>
            {
                var settings = configuration.GetStage(stage);
                return new
                {
                    stage,
                    settings.Prompt,
                    settings.Model,
                    settings.ReasoningEffort,
                    settings.UpdatedAt,
                    allowedPlaceholders = placeholderCatalog.GetForStage(stage)
                };
            })
        });
    }

    [McpServerTool(Name = "save_agent_prompt")]
    [Description("Replaces the complete prompt for one Samwise agent stage while preserving its model and reasoning effort. Read current settings first so intentional guidance and placeholders are not lost.")]
    public async Task<string> SaveAgentPromptAsync(
        [Description("Agent stage: Intake, Planning, or Execution.")]
        string stage,
        [Description("The complete replacement prompt. Any {{placeholders}} must be valid for the selected stage.")]
        string prompt,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<AgentStage>(stage, true, out var parsedStage))
        {
            return SamwiseMcpJson.Serialize(new
            {
                succeeded = false,
                errors = new[]
                {
                    $"Unknown agent stage '{stage}'. Expected Intake, Planning, or Execution."
                }
            });
        }

        var result = await sender.Send(
            new SaveAgentPromptUsecase(parsedStage, prompt),
            cancellationToken);
        return SamwiseMcpJson.Serialize(result);
    }
}

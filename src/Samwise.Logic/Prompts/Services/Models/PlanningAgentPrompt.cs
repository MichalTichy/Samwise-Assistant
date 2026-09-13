using Samwise.Domain.Playbooks;
using Samwise.Logic.Agents;

namespace Samwise.Logic.Prompts.Services;

internal sealed record PlanningAgentPrompt(
    string Text,
    AgentRunConfiguration Configuration,
    ToolCatalogSnapshot PlanningTools,
    ToolCatalogSnapshot ExecutionTools,
    IReadOnlyList<Playbook> AvailablePlaybooks,
    string AgentConfigurationId,
    DateTimeOffset AgentConfigurationUpdatedAt);

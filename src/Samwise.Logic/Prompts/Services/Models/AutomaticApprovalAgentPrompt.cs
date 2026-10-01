using Samwise.Logic.Agents;

namespace Samwise.Logic.Prompts.Services;

internal sealed record AutomaticApprovalAgentPrompt(
    string Text,
    AgentRunConfiguration Configuration,
    AgentRunConfiguration ExecutionConfiguration,
    ToolCatalogSnapshot ExecutionTools);

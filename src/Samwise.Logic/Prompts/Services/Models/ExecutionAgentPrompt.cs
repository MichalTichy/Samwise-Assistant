using Samwise.Logic.Agents;

namespace Samwise.Logic.Prompts.Services;

internal sealed record ExecutionAgentPrompt(
    string Text,
    AgentRunConfiguration Configuration,
    ToolCatalogSnapshot ExecutionTools);

using Samwise.Logic.Agents;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;

namespace Samwise.Logic.Prompts.Services;

internal sealed record IntakeAgentPrompt(
    string Text,
    AgentRunConfiguration Configuration,
    ToolCatalogSnapshot IntakeTools,
    IReadOnlyList<DomainWorkItem> ActiveWorkItems);

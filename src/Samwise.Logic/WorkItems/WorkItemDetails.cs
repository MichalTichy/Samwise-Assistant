using Samwise.Domain.Agents;
using Samwise.Domain.WorkItems;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;

namespace Samwise.Logic.WorkItems;

public sealed record WorkItemDetails(
    DomainWorkItem WorkItem,
    WorkItemPlan? CurrentPlan,
    IReadOnlyList<WorkItemComment> Comments,
    IReadOnlyList<AgentInvocation> Invocations);

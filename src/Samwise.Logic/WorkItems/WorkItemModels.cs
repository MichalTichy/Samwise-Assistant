using Samwise.Domain.Agents;
using Samwise.Domain.WorkItems;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;
using WorkItemStatus = Samwise.Domain.WorkItems.WorkItemStatus;

namespace Samwise.Logic.WorkItems;

public sealed record WorkItemCard(
    string Id,
    string Title,
    string Description,
    WorkItemStatus Status,
    int Revision,
    DateTimeOffset UpdatedAt,
    int? PlanNumber,
    string? UserQuestion);

public sealed record WorkItemDetails(
    DomainWorkItem WorkItem,
    WorkItemPlan? CurrentPlan,
    IReadOnlyList<WorkItemComment> Comments,
    IReadOnlyList<AgentInvocation> Invocations);

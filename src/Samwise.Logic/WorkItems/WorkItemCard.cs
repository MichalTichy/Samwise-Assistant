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
    string? UserQuestion,
    bool AutomaticallyExecuted);

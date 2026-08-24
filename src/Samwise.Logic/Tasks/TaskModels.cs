using Samwise.Domain.Agents;
using Samwise.Domain.Inbox;
using Samwise.Domain.Tasks;

namespace Samwise.Logic.Tasks;

public sealed record TaskCard(
    Guid Id,
    string Title,
    string Description,
    SamwiseTaskStatus Status,
    int Revision,
    DateTimeOffset UpdatedAt,
    int? PlanNumber);

public sealed record TaskDetails(
    SamwiseTask Task,
    TaskPlan? CurrentPlan,
    IReadOnlyList<TaskComment> Comments,
    IReadOnlyList<PlanDecision> Decisions,
    IReadOnlyList<AgentInvocation> Invocations);

public sealed record InboxEventItem(
    Guid Id,
    string Source,
    string Text,
    DateTimeOffset ReceivedAt,
    InboundHandlingKind Handling,
    Guid? TaskId,
    string Explanation);

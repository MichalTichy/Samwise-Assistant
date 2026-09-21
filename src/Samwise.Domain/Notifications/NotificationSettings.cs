using Samwise.Domain.WorkItems;
using Shared.Infrastructure.Persistence;

namespace Samwise.Domain.Notifications;

public sealed class NotificationSettings : IItemWithId
{
    public const string DocumentId = "notifications";

    public string Id { get; set; } = DocumentId;
    public bool NotifyPlanAwaitingApproval { get; set; } = true;
    public bool NotifyQuestionAsked { get; set; } = true;
    public bool NotifyNeedsAttention { get; set; } = true;
    public bool NotifyCompleted { get; set; }

    /// <summary>Whether reaching <paramref name="status"/> should notify the owner.</summary>
    public bool IsEnabled(WorkItemStatus status) => status switch
    {
        WorkItemStatus.AwaitingApproval => NotifyPlanAwaitingApproval,
        WorkItemStatus.AwaitingUserInput => NotifyQuestionAsked,
        WorkItemStatus.NeedsAttention or WorkItemStatus.OutcomeUnknown => NotifyNeedsAttention,
        WorkItemStatus.Completed => NotifyCompleted,
        _ => false
    };
}

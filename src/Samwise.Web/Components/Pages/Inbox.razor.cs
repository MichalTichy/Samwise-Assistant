using MediatR;
using Microsoft.AspNetCore.Components;
using Samwise.Domain.Inbox;
using Samwise.Logic.Inbox;
using Samwise.Logic.Inbox.Specifications;
using Samwise.Logic.Inbox.Usecases;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Web.Components.Pages;

public partial class Inbox
{
    private IReadOnlyList<InboxEventItem> events = [];
    private bool loading;
    private string? retryingEventId;
    private string? actionError;

    [Inject]
    private ISender Sender { get; set; } = default!;

    [Inject]
    private IReadOnlyRepository<InboundEvent> InboundEventRepository { get; set; } = default!;

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        loading = true;
        try
        {
            var inboundEvents = await InboundEventRepository.ListAsync(new InboxEventsSpecification());
            events = inboundEvents.Select(item =>
            {
                return new InboxEventItem(
                    item.Id,
                    item.Source,
                    item.GetIntakeText(),
                    item.ReceivedAt,
                    item.Status,
                    item.Outcomes.Select(outcome => new InboxEventOutcomeItem(
                        outcome.Kind,
                        outcome.WorkItemId,
                        outcome.RelevantText,
                        outcome.Explanation)).ToList(),
                    item.Explanation);
            }).ToList();
        }
        finally
        {
            loading = false;
        }
    }

    private async Task RetryAsync(string inboundEventId)
    {
        retryingEventId = inboundEventId;
        actionError = null;
        try
        {
            await Sender.Send(new RetryInboundEventUsecase(inboundEventId));
            await LoadAsync();
        }
        catch (Exception exception)
        {
            actionError = exception.Message;
        }
        finally
        {
            retryingEventId = null;
        }
    }

    private static string FormatStatus(InboxEventItem item) => item.Status switch
    {
        InboundEventStatus.Pending => "Pending",
        InboundEventStatus.NeedsReview => "Needs review",
        InboundEventStatus.Processed when item.Outcomes.Count == 0 => "Ignored",
        InboundEventStatus.Processed when item.Outcomes.Count == 1 &&
            item.Outcomes[0].Kind == InboundEventOutcomeKind.CreatedWorkItem => "Created work item",
        InboundEventStatus.Processed when item.Outcomes.Count == 1 => "Updated work item",
        InboundEventStatus.Processed => $"Processed · {item.Outcomes.Count} work items",
        _ => item.Status.ToString()
    };

    private static string StatusClass(InboxEventItem item) => item.Status switch
    {
        InboundEventStatus.Pending => "handling-pending",
        InboundEventStatus.NeedsReview => "handling-needsreview",
        InboundEventStatus.Processed when item.Outcomes.Count == 0 => "handling-ignore",
        InboundEventStatus.Processed => "handling-processed",
        _ => string.Empty
    };

    private static string FormatOutcome(InboundEventOutcomeKind kind) => kind switch
    {
        InboundEventOutcomeKind.CreatedWorkItem => "Created",
        InboundEventOutcomeKind.UpdatedWorkItem => "Updated",
        _ => kind.ToString()
    };

    private static string PreviewText(string text)
    {
        const int previewLength = 220;
        var normalized = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= previewLength
            ? normalized
            : $"{normalized[..previewLength].TrimEnd()}…";
    }
}

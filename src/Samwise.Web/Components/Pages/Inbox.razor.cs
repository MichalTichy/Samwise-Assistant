using MediatR;
using Microsoft.AspNetCore.Components;
using Samwise.Domain.Inbox;
using Samwise.Logic.Inbox.Specifications;
using Samwise.Logic.Inbox.Usecases;
using Samwise.Logic.WorkItems;
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
            events = inboundEvents.Select(item => new InboxEventItem(
                item.Id,
                item.Source,
                item.Text,
                item.ReceivedAt,
                item.Handling,
                item.WorkItemId,
                item.Explanation)).ToList();
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

    private static string FormatHandling(InboundHandlingKind handling) => handling switch
    {
        InboundHandlingKind.CreateWorkItem => "Created work item",
        InboundHandlingKind.AppendToWorkItem => "Updated work item",
        InboundHandlingKind.NeedsReview => "Needs review",
        InboundHandlingKind.Ignore => "Ignored",
        _ => handling.ToString()
    };
}

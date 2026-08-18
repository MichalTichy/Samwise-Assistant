using MediatR;
using Microsoft.AspNetCore.Components;
using Samwise.Domain.Inbox;
using Samwise.Logic.Inbox.Usecases;
using Samwise.Logic.Tasks;
using Samwise.Logic.Tasks.Usecases;

namespace Samwise.Web.Components.Pages;

public partial class Inbox
{
    private IReadOnlyList<InboxEventItem> events = [];
    private bool loading;
    private Guid? retryingEventId;
    private string? actionError;

    [Inject]
    private ISender Sender { get; set; } = default!;

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        loading = true;
        try
        {
            events = await Sender.Send(new GetInboxUsecase());
        }
        finally
        {
            loading = false;
        }
    }

    private async Task RetryAsync(Guid inboundEventId)
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
        InboundHandlingKind.CreateTask => "Created task",
        InboundHandlingKind.AppendToTask => "Updated task",
        InboundHandlingKind.NeedsReview => "Needs review",
        InboundHandlingKind.Ignore => "Ignored",
        _ => handling.ToString()
    };
}

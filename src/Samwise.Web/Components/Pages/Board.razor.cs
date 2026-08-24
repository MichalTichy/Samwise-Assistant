using MediatR;
using Microsoft.AspNetCore.Components;
using Samwise.Domain.Tasks;
using Samwise.Logic.Tasks;
using Samwise.Logic.Tasks.Usecases;

namespace Samwise.Web.Components.Pages;

public partial class Board
{
    private static readonly BoardColumn[] Columns =
    [
        new("Draft", "dot-draft", [SamwiseTaskStatus.Planning, SamwiseTaskStatus.NeedsAttention, SamwiseTaskStatus.OutcomeUnknown]),
        new("Approval", "dot-approval", [SamwiseTaskStatus.AwaitingApproval]),
        new("In progress", "dot-running", [SamwiseTaskStatus.Approved, SamwiseTaskStatus.Executing]),
        new("Done", "dot-complete", [SamwiseTaskStatus.Completed, SamwiseTaskStatus.Discarded])
    ];

    private IReadOnlyList<TaskCard> tasks = [];
    private bool loading;

    [Inject]
    private ISender Sender { get; set; } = default!;

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        loading = true;
        try
        {
            tasks = await Sender.Send(new GetBoardUsecase(IncludeArchived: true));
        }
        finally
        {
            loading = false;
        }
    }

    private static string FormatStatus(SamwiseTaskStatus status) => status switch
    {
        SamwiseTaskStatus.AwaitingApproval => "Awaiting approval",
        SamwiseTaskStatus.NeedsAttention => "Needs attention",
        SamwiseTaskStatus.OutcomeUnknown => "Outcome unknown",
        _ => status.ToString()
    };

    private sealed record BoardColumn(string Name, string CssClass, SamwiseTaskStatus[] Statuses);
}

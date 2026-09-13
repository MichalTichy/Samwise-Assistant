using Microsoft.AspNetCore.Components;
using Samwise.Domain.WorkItems;
using Samwise.Logic.WorkItems;
using Samwise.Logic.WorkItems.Specifications;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;
using Task = System.Threading.Tasks.Task;
using WorkItemStatus = Samwise.Domain.WorkItems.WorkItemStatus;

namespace Samwise.Web.Components.Pages;

public partial class Board
{
    private static readonly BoardColumn[] Columns =
    [
        new("Draft", "dot-draft", [WorkItemStatus.Planning, WorkItemStatus.NeedsAttention, WorkItemStatus.OutcomeUnknown]),
        new("Approval", "dot-approval", [WorkItemStatus.AwaitingApproval]),
        new("In progress", "dot-running", [WorkItemStatus.Approved, WorkItemStatus.Executing]),
        new("Follow-up", "dot-follow-up", [WorkItemStatus.WaitingForFollowUp]),
        new("Done", "dot-complete", [WorkItemStatus.Completed, WorkItemStatus.Discarded])
    ];

    private IReadOnlyList<WorkItemCard> workItems = [];
    private bool loading;

    [Inject]
    private IReadOnlyRepository<DomainWorkItem> WorkItemRepository { get; set; } = default!;

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        loading = true;
        try
        {
            var documents = await WorkItemRepository.ListAsync(
                new BoardWorkItemsSpecification(includeArchived: true));
            workItems = documents.Select(workItem =>
            {
                var plan = workItem.FindCurrentPlan();
                return new WorkItemCard(
                    workItem.Id,
                    workItem.Title,
                    workItem.Description,
                    workItem.Status,
                    workItem.Revision,
                    workItem.UpdatedAt,
                    plan?.PlanNumber);
            }).ToList();
        }
        finally
        {
            loading = false;
        }
    }

    private static string FormatStatus(WorkItemStatus status) => status switch
    {
        WorkItemStatus.AwaitingApproval => "Awaiting approval",
        WorkItemStatus.NeedsAttention => "Needs attention",
        WorkItemStatus.OutcomeUnknown => "Outcome unknown",
        WorkItemStatus.WaitingForFollowUp => "Waiting for follow-up",
        _ => status.ToString()
    };

    private sealed record BoardColumn(string Name, string CssClass, WorkItemStatus[] Statuses);
}

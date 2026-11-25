using Samwise.Web.Components;
using Microsoft.AspNetCore.Components;
using Samwise.Domain.WorkItems;
using Samwise.Logic.WorkItems;
using Samwise.Logic.WorkItems.Specifications;
using Samwise.Logic.WorkItems.Usecases;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;
using Task = System.Threading.Tasks.Task;
using WorkItemStatus = Samwise.Domain.WorkItems.WorkItemStatus;

namespace Samwise.Web.Pages.WorkItems;

public partial class Board : AppComponentBase
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    private static readonly BoardColumn[] Columns =
    [
        new("draft", "Draft", "dot-draft", [WorkItemStatus.Planning, WorkItemStatus.NeedsAttention, WorkItemStatus.OutcomeUnknown]),
        new("input", "Needs your input", "dot-input", [WorkItemStatus.AwaitingUserInput]),
        new("approval", "Approval", "dot-approval", [WorkItemStatus.AwaitingApproval]),
        new("progress", "In progress", "dot-running", [WorkItemStatus.Approved, WorkItemStatus.Executing]),
        new("done", "Done", "dot-complete", [WorkItemStatus.Completed])
    ];

    // Phones show one column at a time; these need the owner first when they have cards.
    private static readonly string[] PreferredMobileColumns = ["approval", "input"];

    private IReadOnlyList<WorkItemCard> workItems = [];
    private WorkItemCard? workItemToDiscard;
    private string discardReason = string.Empty;
    private string? discardError;
    private bool discarding;
    private string? defaultMobileColumnKey;

    [Inject]
    private IReadOnlyRepository<DomainWorkItem> WorkItemRepository { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "column")]
    public string? SelectedColumn { get; set; }

    // The tabs exist only on phones, where columns needing the owner come first.
    private static readonly BoardColumn[] MobileColumns = Columns
        .OrderBy(column => Array.IndexOf(PreferredMobileColumns, column.Key) is var index and >= 0 ? index : PreferredMobileColumns.Length)
        .ToArray();

    private string ActiveColumnKey =>
        Columns.Any(column => column.Key == SelectedColumn)
            ? SelectedColumn!
            : defaultMobileColumnKey ?? ChooseDefaultMobileColumnKey();

    private string ChooseDefaultMobileColumnKey() =>
        PreferredMobileColumns.FirstOrDefault(key => CardsFor(Columns.Single(column => column.Key == key)).Count > 0)
            ?? Columns.FirstOrDefault(column => CardsFor(column).Count > 0)?.Key
            ?? Columns[0].Key;

    private IReadOnlyList<WorkItemCard> CardsFor(BoardColumn column) =>
        workItems.Where(workItem => column.Statuses.Contains(workItem.Status)).ToList();

    private void SelectColumn(string key) =>
        Navigation.NavigateTo(Navigation.GetUriWithQueryParameter("column", key), replace: true);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await LoadAsync();
            if (ComponentToken.IsCancellationRequested)
            {
                return;
            }

            defaultMobileColumnKey = ChooseDefaultMobileColumnKey();
            StartRefreshing(RefreshInterval, RefreshCardsAsync);
            StateHasChanged();
        }
    }

    private Task LoadAsync() => ExecuteLoadAsync(ReadCardsAsync, cards => workItems = cards);

    private async Task RefreshCardsAsync(CancellationToken token)
    {
        if (IsLoading || discarding)
        {
            return;
        }

        var displayed = workItems;
        var cards = await ReadCardsAsync(token);
        if (!token.IsCancellationRequested && !IsLoading && !discarding && ReferenceEquals(displayed, workItems) &&
            !workItems.SequenceEqual(cards))
        {
            workItems = cards;
            StateHasChanged();
        }
    }

    private async Task<IReadOnlyList<WorkItemCard>> ReadCardsAsync(CancellationToken token)
    {
        var documents = await WorkItemRepository.ListAsync(new BoardWorkItemsSpecification(), token);
        return documents.Select(workItem =>
        {
            var plan = workItem.FindCurrentPlan();
            return new WorkItemCard(
                workItem.Id,
                workItem.Title,
                workItem.Description,
                workItem.Status,
                workItem.Revision,
                workItem.UpdatedAt,
                plan?.PlanNumber,
                workItem.PendingPlanningQuestion?.Prompt,
                workItem.WasAutomaticallyExecuted());
        }).ToList();
    }

    private void OpenDiscardDialog(WorkItemCard workItem)
    {
        if (discarding)
        {
            return;
        }

        workItemToDiscard = workItem;
        discardReason = string.Empty;
        discardError = null;
    }

    private void CloseDiscardDialog()
    {
        if (discarding)
        {
            return;
        }

        workItemToDiscard = null;
        discardReason = string.Empty;
        discardError = null;
    }

    private Task HandleDiscardDialogOpenChangedAsync(bool open)
    {
        if (!open)
        {
            CloseDiscardDialog();
        }

        return Task.CompletedTask;
    }

    private Task DiscardAsync() => ExecuteActionAsync(async () =>
    {
        if (IsLoading || discarding || workItemToDiscard is not { } workItem)
        {
            return;
        }

        discarding = true;
        discardError = null;
        try
        {
            await SendAsync(new DiscardWorkItemUsecase(workItem.Id, discardReason));
            workItems = workItems.Where(card => card.Id != workItem.Id).ToList();
            workItemToDiscard = null;
            discardReason = string.Empty;
        }
        catch (Exception exception) when (!ComponentToken.IsCancellationRequested)
        {
            discardError = exception.Message;
        }
        finally
        {
            discarding = false;
        }
    });

    private static string FormatStatus(WorkItemStatus status) => status switch
    {
        WorkItemStatus.AwaitingApproval => "Awaiting approval",
        WorkItemStatus.AwaitingUserInput => "Needs your input",
        WorkItemStatus.NeedsAttention => "Needs attention",
        WorkItemStatus.OutcomeUnknown => "Outcome unknown",
        _ => status.ToString()
    };

    private sealed record BoardColumn(string Key, string Name, string CssClass, WorkItemStatus[] Statuses);
}

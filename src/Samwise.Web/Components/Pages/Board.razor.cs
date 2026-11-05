using MediatR;
using Microsoft.AspNetCore.Components;
using Samwise.Domain.WorkItems;
using Samwise.Logic.WorkItems;
using Samwise.Logic.WorkItems.Specifications;
using Samwise.Logic.WorkItems.Usecases;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;
using Task = System.Threading.Tasks.Task;
using WorkItemStatus = Samwise.Domain.WorkItems.WorkItemStatus;

namespace Samwise.Web.Components.Pages;

public partial class Board : IAsyncDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    private static readonly BoardColumn[] Columns =
    [
        new("draft", "Draft", "dot-draft", [WorkItemStatus.Planning, WorkItemStatus.NeedsAttention, WorkItemStatus.OutcomeUnknown]),
        new("input", "Needs your input", "dot-input", [WorkItemStatus.AwaitingUserInput]),
        new("approval", "Approval", "dot-approval", [WorkItemStatus.AwaitingApproval]),
        new("progress", "In progress", "dot-running", [WorkItemStatus.Approved, WorkItemStatus.Executing, WorkItemStatus.AwaitingExternalEvent]),
        new("done", "Done", "dot-complete", [WorkItemStatus.Completed])
    ];

    // Phones show one column at a time; these need the owner first when they have cards.
    private static readonly string[] PreferredMobileColumns = ["approval", "input"];

    private IReadOnlyList<WorkItemCard> workItems = [];
    private bool loading = true;
    private WorkItemCard? workItemToDiscard;
    private string discardReason = string.Empty;
    private string? discardError;
    private bool discarding;
    private string? defaultMobileColumnKey;
    private readonly SemaphoreSlim loadGate = new(1, 1);
    private CancellationTokenSource? refreshCancellation;
    private Task? refreshTask;

    [Inject]
    private ISender Sender { get; set; } = default!;

    [Inject]
    private IReadOnlyRepository<DomainWorkItem> WorkItemRepository { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private ILogger<Board> Logger { get; set; } = default!;

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
            defaultMobileColumnKey = ChooseDefaultMobileColumnKey();
            refreshCancellation = new CancellationTokenSource();
            refreshTask = RefreshPeriodicallyAsync(refreshCancellation.Token);
            StateHasChanged();
        }
    }

    private async Task RefreshPeriodicallyAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(RefreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await InvokeAsync(async () =>
                    {
                        if (!loading && await RefreshCardsAsync(cancellationToken))
                        {
                            StateHasChanged();
                        }
                    });
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    Logger.LogWarning(exception, "Refreshing the board failed");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Navigation stopped the board refresh.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (refreshCancellation is null)
        {
            return;
        }

        await refreshCancellation.CancelAsync();
        if (refreshTask is not null)
        {
            await refreshTask;
        }

        refreshCancellation.Dispose();
    }

    private async Task LoadAsync()
    {
        loading = true;
        try
        {
            await RefreshCardsAsync(CancellationToken.None);
        }
        finally
        {
            loading = false;
        }
    }

    private async Task<bool> RefreshCardsAsync(CancellationToken cancellationToken)
    {
        await loadGate.WaitAsync(cancellationToken);
        try
        {
            var documents = await WorkItemRepository.ListAsync(
                new BoardWorkItemsSpecification(), cancellationToken);
            var cards = documents.Select(workItem =>
            {
                var plan = workItem.FindCurrentPlan();
                return new WorkItemCard(
                    workItem.Id,
                    workItem.Title,
                    workItem.WaitingReason ?? workItem.Description,
                    workItem.Status,
                    workItem.Revision,
                    workItem.UpdatedAt,
                    plan?.PlanNumber,
                    workItem.PendingPlanningQuestion?.Prompt,
                    workItem.WasAutomaticallyExecuted());
            }).ToList();

            if (workItems.SequenceEqual(cards))
            {
                return false;
            }

            workItems = cards;
            return true;
        }
        finally
        {
            loadGate.Release();
        }
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

    private async Task DiscardAsync()
    {
        if (discarding || workItemToDiscard is not { } workItem)
        {
            return;
        }

        discarding = true;
        discardError = null;
        await loadGate.WaitAsync();
        try
        {
            await Sender.Send(new DiscardWorkItemUsecase(workItem.Id, discardReason));
            workItems = workItems.Where(card => card.Id != workItem.Id).ToList();
            workItemToDiscard = null;
            discardReason = string.Empty;
        }
        catch (Exception exception)
        {
            discardError = exception.Message;
        }
        finally
        {
            loadGate.Release();
            discarding = false;
        }
    }

    private static string FormatStatus(WorkItemStatus status) => status switch
    {
        WorkItemStatus.AwaitingApproval => "Awaiting approval",
        WorkItemStatus.AwaitingUserInput => "Needs your input",
        WorkItemStatus.AwaitingExternalEvent => Resources.WorkItemResources.AwaitingExternalEvent,
        WorkItemStatus.NeedsAttention => "Needs attention",
        WorkItemStatus.OutcomeUnknown => "Outcome unknown",
        _ => status.ToString()
    };

    private sealed record BoardColumn(string Key, string Name, string CssClass, WorkItemStatus[] Statuses);
}

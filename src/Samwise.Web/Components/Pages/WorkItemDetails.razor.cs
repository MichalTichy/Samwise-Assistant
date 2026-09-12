using MediatR;
using Microsoft.AspNetCore.Components;
using Samwise.Domain.Agents;
using Samwise.Domain.WorkItems;
using Samwise.Logic.WorkItems.Specifications;
using Samwise.Logic.WorkItems.Usecases;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;
using WorkItemDetailsModel = Samwise.Logic.WorkItems.WorkItemDetails;
using Task = System.Threading.Tasks.Task;
using WorkItemStatus = Samwise.Domain.WorkItems.WorkItemStatus;

namespace Samwise.Web.Components.Pages;

public partial class WorkItemDetails
{
    private WorkItemDetailsModel? details;
    private bool loading = true;
    private bool actionBusy;
    private string contextText = string.Empty;
    private string rejectionFeedback = string.Empty;
    private string? actionError;
    private AgentInvocation? selectedInvocation;
    private bool activityDetailsOpen;

    [Inject]
    private ISender Sender { get; set; } = default!;

    [Inject]
    private IReadOnlyRepository<DomainWorkItem> WorkItemRepository { get; set; } = default!;

    [Inject]
    private IReadOnlyRepository<AgentInvocation> InvocationRepository { get; set; } = default!;

    [Parameter]
    public string WorkItemId { get; set; } = string.Empty;

    protected override Task OnParametersSetAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        loading = true;
        try
        {
            var workItem = await WorkItemRepository.GetByIdAsync(WorkItemId);
            if (workItem is null)
            {
                details = null;
                return;
            }

            var invocations = await InvocationRepository.ListAsync(
                new WorkItemInvocationsSpecification(workItem.Id));
            details = new WorkItemDetailsModel(
                workItem,
                workItem.FindCurrentPlan(),
                workItem.Comments,
                invocations);
        }
        finally
        {
            loading = false;
        }
    }

    private Task ApproveAsync()
    {
        var workItem = RequireDetails().WorkItem;
        var plan = RequireDetails().CurrentPlan ?? throw new InvalidOperationException("There is no current plan.");
        return RunActionAsync(() => Sender.Send(new ApproveWorkItemPlanUsecase(workItem.Id, plan.Id, workItem.Revision)));
    }

    private async Task RejectAsync()
    {
        if (string.IsNullOrWhiteSpace(rejectionFeedback))
        {
            actionError = "Add feedback before rejecting the plan.";
            return;
        }

        var workItem = RequireDetails().WorkItem;
        var plan = RequireDetails().CurrentPlan ?? throw new InvalidOperationException("There is no current plan.");
        await RunActionAsync(() => Sender.Send(new RejectWorkItemPlanUsecase(
            workItem.Id,
            plan.Id,
            workItem.Revision,
            rejectionFeedback)));
        rejectionFeedback = string.Empty;
    }

    private async Task AddContextAsync()
    {
        if (string.IsNullOrWhiteSpace(contextText))
        {
            actionError = "Write a comment before adding context.";
            return;
        }

        var text = contextText;
        await RunActionAsync(() => Sender.Send(new AddWorkItemContextUsecase(WorkItemId, text)));
        contextText = string.Empty;
    }

    private Task DiscardAsync() => RunActionAsync(() => Sender.Send(new DiscardWorkItemUsecase(WorkItemId)));

    private Task RetryPlanningAsync() => RunActionAsync(() => Sender.Send(new RetryWorkItemPlanningUsecase(WorkItemId)));

    private async Task RunActionAsync(Func<Task> action)
    {
        actionBusy = true;
        actionError = null;
        try
        {
            await action();
            await LoadAsync();
        }
        catch (Exception exception)
        {
            actionError = exception.Message;
        }
        finally
        {
            actionBusy = false;
        }
    }

    private WorkItemDetailsModel RequireDetails() =>
        details ?? throw new InvalidOperationException("The work item is not loaded.");

    private void ShowInvocation(AgentInvocation invocation)
    {
        selectedInvocation = invocation;
        activityDetailsOpen = true;
    }

    private static string FormatStatus(WorkItemStatus status) => status switch
    {
        WorkItemStatus.AwaitingApproval => "Awaiting approval",
        WorkItemStatus.NeedsAttention => "Needs attention",
        WorkItemStatus.OutcomeUnknown => "Outcome unknown",
        _ => status.ToString()
    };

    private static string FormatAuthor(CommentAuthorKind author) => author switch
    {
        CommentAuthorKind.User => "You",
        CommentAuthorKind.Samwise => "Samwise",
        _ => "System"
    };

    private static string AuthorInitial(CommentAuthorKind author) => author switch
    {
        CommentAuthorKind.User => "Y",
        CommentAuthorKind.Samwise => "S",
        _ => "•"
    };

    private static string FormatEffort(string effort) => effort switch
    {
        "xhigh" => "Extra high reasoning",
        _ when effort.Length > 0 => $"{char.ToUpperInvariant(effort[0])}{effort[1..]} reasoning",
        _ => "Reasoning effort not recorded"
    };
}

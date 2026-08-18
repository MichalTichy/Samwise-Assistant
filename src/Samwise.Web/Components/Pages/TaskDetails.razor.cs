using MediatR;
using Microsoft.AspNetCore.Components;
using Samwise.Domain.Tasks;
using Samwise.Logic.Tasks.Usecases;
using TaskDetailsModel = Samwise.Logic.Tasks.TaskDetails;

namespace Samwise.Web.Components.Pages;

public partial class TaskDetails
{
    private TaskDetailsModel? details;
    private bool loading = true;
    private bool actionBusy;
    private string contextText = string.Empty;
    private string rejectionFeedback = string.Empty;
    private string? actionError;

    [Inject]
    private ISender Sender { get; set; } = default!;

    [Parameter]
    public Guid TaskId { get; set; }

    protected override Task OnParametersSetAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        loading = true;
        try
        {
            details = await Sender.Send(new GetTaskDetailsUsecase(TaskId));
        }
        finally
        {
            loading = false;
        }
    }

    private Task ApproveAsync()
    {
        var task = RequireDetails().Task;
        var plan = RequireDetails().CurrentPlan ?? throw new InvalidOperationException("There is no current plan.");
        return RunActionAsync(() => Sender.Send(new ApproveTaskPlanUsecase(task.Id, plan.Id, task.Revision)));
    }

    private async Task RejectAsync()
    {
        if (string.IsNullOrWhiteSpace(rejectionFeedback))
        {
            actionError = "Add feedback before rejecting the plan.";
            return;
        }

        var task = RequireDetails().Task;
        var plan = RequireDetails().CurrentPlan ?? throw new InvalidOperationException("There is no current plan.");
        await RunActionAsync(() => Sender.Send(new RejectTaskPlanUsecase(
            task.Id,
            plan.Id,
            task.Revision,
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
        await RunActionAsync(() => Sender.Send(new AddTaskContextUsecase(TaskId, text)));
        contextText = string.Empty;
    }

    private Task DiscardAsync() => RunActionAsync(() => Sender.Send(new DiscardTaskUsecase(TaskId)));

    private Task RetryPlanningAsync() => RunActionAsync(() => Sender.Send(new RetryTaskPlanningUsecase(TaskId)));

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

    private TaskDetailsModel RequireDetails() =>
        details ?? throw new InvalidOperationException("The task is not loaded.");

    private static string FormatStatus(SamwiseTaskStatus status) => status switch
    {
        SamwiseTaskStatus.AwaitingApproval => "Awaiting approval",
        SamwiseTaskStatus.NeedsAttention => "Needs attention",
        SamwiseTaskStatus.OutcomeUnknown => "Outcome unknown",
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
}

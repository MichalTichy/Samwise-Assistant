using Samwise.Web.Components;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.AspNetCore.Components;
using Samwise.Domain.WorkItems;
using Samwise.Logic.WorkItems.Usecases;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;
using WorkItemDetailsModel = Samwise.Logic.WorkItems.WorkItemDetails;
using Task = System.Threading.Tasks.Task;
using WorkItemStatus = Samwise.Domain.WorkItems.WorkItemStatus;

namespace Samwise.Web.Pages.WorkItems;

public partial class WorkItemDetails : AppComponentBase
{
    private const string OtherPlanningAnswer = "__other__";
    private WorkItemDetailsModel? details;
    private bool actionBusy;
    private string contextText = string.Empty;
    private string rejectionFeedback = string.Empty;
    private string? actionError;
    private bool decisionSheetOpen;
    private string selectedPlanningAnswer = string.Empty;
    private string otherPlanningAnswer = string.Empty;
    private string? loadedPlanningQuestionId;

    [Inject]
    private ISender Sender { get; set; } = default!;

    [Inject]
    private IReadOnlyRepository<DomainWorkItem> WorkItemRepository { get; set; } = default!;

    [Parameter]
    public string WorkItemId { get; set; } = string.Empty;

    protected override Task OnParametersSetAsync() => LoadAsync();

    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            StartRefreshing(TimeSpan.FromSeconds(3), RefreshChangedWorkItemAsync);
        }

        return Task.CompletedTask;
    }

    private Task LoadAsync()
    {
        var id = WorkItemId;
        return ExecuteLoadAsync(token => WorkItemRepository.GetByIdAsync(id, token), ApplyWorkItem);
    }

    private void ApplyWorkItem(DomainWorkItem? workItem)
    {
        details = workItem is null ? null : new WorkItemDetailsModel(
            workItem, workItem.FindCurrentPlan(), workItem.Comments, []);
        if (loadedPlanningQuestionId != workItem?.PendingPlanningQuestion?.Id)
        {
            loadedPlanningQuestionId = workItem?.PendingPlanningQuestion?.Id;
            selectedPlanningAnswer = string.Empty;
            otherPlanningAnswer = string.Empty;
        }
    }

    private async Task RefreshChangedWorkItemAsync(CancellationToken token)
    {
        if (IsLoading || details is null)
        {
            return;
        }

        var id = WorkItemId;
        var displayed = details.WorkItem;
        var current = await WorkItemRepository.GetByIdAsync(id, token);
        if (!token.IsCancellationRequested && !IsLoading && id == WorkItemId &&
            ReferenceEquals(details?.WorkItem, displayed) && current is not null &&
            (current.UpdatedAt != displayed.UpdatedAt || current.Version != displayed.Version ||
                current.Status != displayed.Status))
        {
            ApplyWorkItem(current);
            StateHasChanged();
        }
    }

    private Task ApproveAsync()
    {
        var workItem = RequireDetails().WorkItem;
        var plan = RequireDetails().CurrentPlan ?? throw new InvalidOperationException("There is no current plan.");
        return RunActionAsync(() => Sender.Send(new ApproveWorkItemPlanUsecase(workItem.Id, plan.Id, workItem.Revision)));
    }

    private void OpenDecisionSheet()
    {
        actionError = null;
        decisionSheetOpen = true;
    }

    private void CloseDecisionSheet() => decisionSheetOpen = false;

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

    private Task ReplanAsync() => RunActionAsync(() => Sender.Send(new ReplanWorkItemUsecase(WorkItemId)));

    private Task RestoreAsync() => RunActionAsync(() => Sender.Send(new RestoreWorkItemUsecase(WorkItemId)));

    private Task ReopenCompletedAsync() => RunActionAsync(() => Sender.Send(new ReopenCompletedWorkItemUsecase(
        WorkItemId,
        "The intended outcome remains unfinished; reassess the current external state before planning.")));

    private Task RetryExecutionAsync() =>
        RunActionAsync(() => Sender.Send(new RetryWorkItemExecutionUsecase(WorkItemId)));

    private void SelectPlanningAnswer(string answer)
    {
        selectedPlanningAnswer = answer;
        actionError = null;
        if (answer != OtherPlanningAnswer)
        {
            otherPlanningAnswer = string.Empty;
        }
    }

    private async Task AnswerPlanningQuestionAsync()
    {
        var question = RequireDetails().WorkItem.PendingPlanningQuestion
            ?? throw new InvalidOperationException("There is no current planning question.");
        if (string.IsNullOrWhiteSpace(selectedPlanningAnswer))
        {
            actionError = "Choose an answer before continuing.";
            return;
        }

        if (selectedPlanningAnswer == OtherPlanningAnswer && string.IsNullOrWhiteSpace(otherPlanningAnswer))
        {
            actionError = "Write your answer before continuing.";
            return;
        }

        await RunActionAsync(() => Sender.Send(new AnswerPlanningQuestionUsecase(
            WorkItemId,
            question.Id,
            selectedPlanningAnswer == OtherPlanningAnswer ? null : selectedPlanningAnswer,
            selectedPlanningAnswer == OtherPlanningAnswer ? otherPlanningAnswer : null)));
    }

    private async Task RunActionAsync(Func<Task> action)
    {
        actionBusy = true;
        actionError = null;
        try
        {
            await action();
            decisionSheetOpen = false;
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

    private static string FormatStatus(WorkItemStatus status) => status switch
    {
        WorkItemStatus.AwaitingApproval => "Awaiting approval",
        WorkItemStatus.AwaitingUserInput => "Needs your input",
        WorkItemStatus.NeedsAttention => "Needs attention",
        WorkItemStatus.OutcomeUnknown => "Outcome unknown",
        _ => status.ToString()
    };

    private static string FormatToolCallCount(int count) =>
        count == 1 ? "1 tool call" : $"{count} tool calls";

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

    private static IReadOnlyList<WorkItemComment> ConversationComments(
        IReadOnlyList<WorkItemComment> comments,
        bool hasCompletionCard) => hasCompletionCard
        ? comments.Where(comment => comment.Kind != WorkItemCommentKind.ExecutionUpdate).ToList()
        : comments;

    private static CompletionDisplay? ResolveCompletion(
        WorkItemCompletion? completion,
        IReadOnlyList<WorkItemComment> comments)
    {
        if (completion is not null)
        {
            return new CompletionDisplay(
                completion.Summary,
                completion.Outputs,
                completion.CompletedAt);
        }

        var update = comments
            .Where(comment => comment.Kind == WorkItemCommentKind.ExecutionUpdate)
            .MaxBy(comment => comment.CreatedAt);
        if (update is null)
        {
            return null;
        }

        var normalized = update.Body.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        var marker = "\n\nOutputs:\n";
        var markerIndex = normalized.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return new CompletionDisplay(normalized, [], update.CreatedAt);
        }

        var summary = normalized[..markerIndex].Trim();
        var outputs = normalized[(markerIndex + marker.Length)..]
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(RemoveListMarker)
            .ToList();
        return new CompletionDisplay(summary, outputs, update.CreatedAt);
    }

    private static string RemoveListMarker(string value) => Regex.Replace(
        value,
        @"^\s*(?:(?:\d+[.)])|[-*•])\s+",
        string.Empty);

    internal static IReadOnlyList<string> ReadableParagraphs(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var blocks = Regex.Split(
                text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim(),
                "\\n\\s*\\n")
            .Where(block => !string.IsNullOrWhiteSpace(block));
        var paragraphs = new List<string>();
        foreach (var block in blocks)
        {
            var normalized = block.Trim();
            if (normalized.Length <= 320 || normalized.Contains('\n'))
            {
                paragraphs.Add(normalized);
                continue;
            }

            var sentences = Regex.Split(normalized, @"(?<=[.!?])\s+(?=[A-Z0-9])");
            var paragraph = string.Empty;
            foreach (var sentence in sentences)
            {
                if (paragraph.Length > 0 && paragraph.Length + sentence.Length > 320)
                {
                    paragraphs.Add(paragraph);
                    paragraph = sentence;
                }
                else
                {
                    paragraph = string.IsNullOrEmpty(paragraph)
                        ? sentence
                        : $"{paragraph} {sentence}";
                }
            }

            if (!string.IsNullOrWhiteSpace(paragraph))
            {
                paragraphs.Add(paragraph);
            }
        }

        return paragraphs;
    }

    private sealed record CompletionDisplay(
        string Summary,
        IReadOnlyList<string> Outputs,
        DateTimeOffset CompletedAt);
}

using System.Text.RegularExpressions;
using MediatR;
using Microsoft.AspNetCore.Components;
using Samwise.Domain.Agents;
using Samwise.Domain.Inbox;
using Samwise.Domain.WorkItems;
using Samwise.Logic.WorkItems.Specifications;
using Samwise.Logic.WorkItems.Usecases;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;
using WorkItemDetailsModel = Samwise.Logic.WorkItems.WorkItemDetails;
using Task = System.Threading.Tasks.Task;
using WorkItemStatus = Samwise.Domain.WorkItems.WorkItemStatus;

namespace Samwise.Web.Components.Pages;

public partial class WorkItemDetails : IAsyncDisposable
{
    private const string OtherPlanningAnswer = "__other__";
    private WorkItemDetailsModel? details;
    private bool loading = true;
    private bool loadingInvocations;
    private bool loadingSources;
    private string? invocationLoadStartedFor;
    private string? sourceLoadStartedFor;
    private string? invocationLoadError;
    private string? sourceLoadError;
    private IReadOnlyList<InboundEvent> sourceEvents = [];
    private int loadVersion;
    private bool actionBusy;
    private string contextText = string.Empty;
    private string rejectionFeedback = string.Empty;
    private string? actionError;
    private AgentInvocation? selectedInvocation;
    private bool activityDetailsOpen;
    private bool decisionSheetOpen;
    private string selectedPlanningAnswer = string.Empty;
    private string otherPlanningAnswer = string.Empty;
    private string? loadedPlanningQuestionId;
    private readonly SemaphoreSlim loadGate = new(1, 1);
    private CancellationTokenSource? refreshCancellation;
    private Task? refreshTask;

    [Inject]
    private ISender Sender { get; set; } = default!;

    [Inject]
    private IReadOnlyRepository<DomainWorkItem> WorkItemRepository { get; set; } = default!;

    [Inject]
    private IReadOnlyRepository<AgentInvocation> InvocationRepository { get; set; } = default!;

    [Inject]
    private IReadOnlyRepository<InboundEvent> InboundEventRepository { get; set; } = default!;

    [Inject]
    private ILogger<WorkItemDetails> Logger { get; set; } = default!;

    [Parameter]
    public string WorkItemId { get; set; } = string.Empty;

    protected override Task OnParametersSetAsync() => LoadAsync();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            refreshCancellation = new CancellationTokenSource();
            refreshTask = RefreshChangedWorkItemAsync(refreshCancellation.Token);
        }

        if (details is not null && loadingSources && sourceLoadStartedFor != details.WorkItem.Id)
        {
            var sourceWorkItemId = details.WorkItem.Id;
            var sourceVersion = loadVersion;
            var sourceIds = details.WorkItem.Sources
                .Select(source => source.InboundEventId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .Reverse()
                .ToList();
            sourceLoadStartedFor = sourceWorkItemId;
            try
            {
                var loaded = new List<InboundEvent>();
                foreach (var sourceId in sourceIds)
                {
                    var source = await InboundEventRepository.GetByIdAsync(sourceId);
                    if (source is not null)
                    {
                        loaded.Add(source);
                    }
                }

                if (sourceVersion == loadVersion && details?.WorkItem.Id == sourceWorkItemId)
                {
                    sourceEvents = loaded;
                }
            }
            catch (Exception exception)
            {
                if (sourceVersion == loadVersion)
                {
                    sourceLoadError = exception.Message;
                }
            }
            finally
            {
                if (sourceVersion == loadVersion)
                {
                    loadingSources = false;
                    StateHasChanged();
                }
            }
        }

        if (details is null || !loadingInvocations || invocationLoadStartedFor == details.WorkItem.Id)
        {
            return;
        }

        var workItemId = details.WorkItem.Id;
        var version = loadVersion;
        invocationLoadStartedFor = workItemId;
        try
        {
            var invocations = await InvocationRepository.ListAsync(
                new WorkItemInvocationsSpecification(workItemId));
            if (version == loadVersion && details?.WorkItem.Id == workItemId)
            {
                details = details with { Invocations = invocations };
            }
        }
        catch (Exception exception)
        {
            if (version == loadVersion)
            {
                invocationLoadError = exception.Message;
            }
        }
        finally
        {
            if (version == loadVersion)
            {
                loadingInvocations = false;
                StateHasChanged();
            }
        }
    }

    private async Task LoadAsync()
    {
        loading = true;
        await loadGate.WaitAsync();
        try
        {
            var workItemId = WorkItemId;
            var workItem = await WorkItemRepository.GetByIdAsync(workItemId);
            if (workItemId != WorkItemId)
            {
                return;
            }

            loadVersion++;
            if (workItem is null)
            {
                details = null;
                sourceEvents = [];
                loadingSources = false;
                loadingInvocations = false;
                return;
            }

            details = new WorkItemDetailsModel(
                workItem,
                workItem.FindCurrentPlan(),
                workItem.Comments,
                []);
            sourceEvents = [];
            loadingSources = workItem.Sources.Count > 0;
            sourceLoadStartedFor = null;
            sourceLoadError = null;
            loadingInvocations = true;
            invocationLoadStartedFor = null;
            invocationLoadError = null;
            if (loadedPlanningQuestionId != workItem.PendingPlanningQuestion?.Id)
            {
                loadedPlanningQuestionId = workItem.PendingPlanningQuestion?.Id;
                selectedPlanningAnswer = string.Empty;
                otherPlanningAnswer = string.Empty;
            }
        }
        finally
        {
            loading = false;
            loadGate.Release();
        }
    }

    private async Task RefreshChangedWorkItemAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await InvokeAsync(async () =>
                    {
                        if (loading || details is null)
                        {
                            return;
                        }

                        var workItemId = WorkItemId;
                        var displayed = details.WorkItem;
                        var current = await WorkItemRepository.GetByIdAsync(workItemId, cancellationToken);
                        if (current is not null && workItemId == WorkItemId &&
                            (current.UpdatedAt != displayed.UpdatedAt ||
                             current.Version != displayed.Version ||
                             current.Status != displayed.Status))
                        {
                            await LoadAsync();
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
                    Logger.LogWarning(exception, "Refreshing work item {WorkItemId} failed", WorkItemId);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Navigation stopped the page refresh.
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

    private void ShowInvocation(AgentInvocation invocation)
    {
        selectedInvocation = invocation;
        activityDetailsOpen = true;
    }

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

    private static string FormatEffort(string effort) => effort switch
    {
        "xhigh" => "Extra high reasoning",
        _ when effort.Length > 0 => $"{char.ToUpperInvariant(effort[0])}{effort[1..]} reasoning",
        _ => "Reasoning effort not recorded"
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

    private static IReadOnlyList<string> ReadableParagraphs(string text)
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

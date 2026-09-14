using System.Text.Json;
using System.Text.RegularExpressions;
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

    private Task ReplanAsync() => RunActionAsync(() => Sender.Send(new ReplanWorkItemUsecase(WorkItemId)));

    private Task RetryExecutionAsync() =>
        RunActionAsync(() => Sender.Send(new RetryWorkItemExecutionUsecase(WorkItemId)));

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

    private static string PrettyPrintJson(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (JsonException)
        {
            return value;
        }
    }

    private sealed record CompletionDisplay(
        string Summary,
        IReadOnlyList<string> Outputs,
        DateTimeOffset CompletedAt);
}

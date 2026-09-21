using MediatR;
using Samwise.Domain.Inbox;
using Samwise.Inputs.Todoist.Configuration;
using Samwise.Inputs.Todoist.InboundEvents;
using Samwise.Inputs.Todoist.Ingestion;
using Samwise.Logic.Inbox.Services;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Inputs.Todoist.Usecases.Processing;

public sealed record ProcessTodoistInputsUsecase : IRequest<TodoistProcessingResult>;

internal sealed class ProcessTodoistInputsUsecaseHandler(
    IRepository<TodoistInputConfiguration> configurationRepository,
    IReadOnlyRepository<InboundEvent> inboundEventRepository,
    ITodoistTaskReader reader,
    IInboundEventCaptureService captureService,
    ITodoistInputProcessingLock processingLock,
    TimeProvider timeProvider)
    : IRequestHandler<ProcessTodoistInputsUsecase, TodoistProcessingResult>
{
    private const int MaximumPriorOccurrences = 5;

    public async Task<TodoistProcessingResult> Handle(
        ProcessTodoistInputsUsecase request,
        CancellationToken cancellationToken)
    {
        using var processingLease = await processingLock.EnterAsync(cancellationToken);
        var configuration = await configurationRepository.GetRequiredByIdAsync(
            TodoistInputConfiguration.DocumentId,
            cancellationToken);
        if (!configuration.IsConfigured)
        {
            return new TodoistProcessingResult(0, 0, 0, []);
        }

        var now = timeProvider.GetUtcNow();
        TodoistTaskReadResult readResult;
        try
        {
            readResult = await reader.ReadDueTasksAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            configuration.RecordFailedCheck(exception.Message);
            await configurationRepository.UpdateAsync(configuration, cancellationToken);
            return new TodoistProcessingResult(0, 0, 0, [exception.Message]);
        }

        var imported = 0;
        var ignored = 0;
        var alreadyProcessed = 0;
        var errors = new List<string>();
        var dueTasks = readResult.Tasks
            .Select(task => (Task: task, DueAt: task.Due.ResolveDueAt(readResult.AccountTimeZone)))
            .Where(item => item.DueAt is not null && configuration.IsWithinWindow(item.DueAt.Value, now))
            .OrderBy(item => item.DueAt);
        foreach (var (task, dueAt) in dueTasks)
        {
            var contextKey = TodoistTaskInboundEvent.CreateContextKey(task.TaskId);
            var previousEvents = (await inboundEventRepository.ListAsync(
                    new TodoistTaskEventsSpecification(contextKey),
                    cancellationToken))
                .OfType<TodoistTaskInboundEvent>()
                .Where(item => item.ContextKey == contextKey)
                .ToList();
            var inboundEvent = CreateEvent(task, dueAt!.Value, contextKey, configuration, now);
            var previousOccurrence = previousEvents.FirstOrDefault(
                item => item.ExternalId == inboundEvent.ExternalId);
            if (previousOccurrence is { Status: not InboundEventStatus.Pending })
            {
                alreadyProcessed++;
                continue;
            }

            if (previousOccurrence is { Status: InboundEventStatus.Pending })
            {
                try
                {
                    await captureService.CaptureAsync(previousOccurrence, cancellationToken);
                    alreadyProcessed++;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    errors.Add($"Todoist task {task.TaskId} could not be processed: {exception.Message}");
                }

                continue;
            }

            var isIgnored = IsIgnored(task, configuration);
            if (isIgnored)
            {
                inboundEvent.Status = InboundEventStatus.Processed;
                inboundEvent.HandledAt = now;
                inboundEvent.Explanation = "Ignored by the Todoist input configuration.";
            }
            else
            {
                try
                {
                    inboundEvent.Comments = (await reader.ReadCommentsAsync(task.TaskId, cancellationToken))
                        .Select(comment => new TodoistTaskComment
                        {
                            CommentId = comment.CommentId,
                            PostedAt = comment.PostedAt,
                            Content = comment.Content
                        })
                        .ToList();
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Leave the task for the next check rather than capturing it without its comments.
                    errors.Add($"Comments for Todoist task {task.TaskId} could not be read: {exception.Message}");
                    continue;
                }

                inboundEvent.Context = BuildContext(previousEvents);
            }

            InboundEventCaptureResult capture;
            try
            {
                capture = await captureService.CaptureAsync(inboundEvent, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One failed capture must not prevent later due tasks from being captured.
                // If persistence succeeded before an enqueue failure, the shared worker
                // will recover the pending event on its next sweep.
                errors.Add($"Todoist task {task.TaskId} could not be captured: {exception.Message}");
                continue;
            }
            if (capture.AlreadyExisted)
            {
                alreadyProcessed++;
            }
            else if (isIgnored)
            {
                ignored++;
            }
            else
            {
                imported++;
            }
        }

        if (errors.Count == 0)
        {
            configuration.RecordSuccessfulCheck(now);
        }
        else
        {
            configuration.RecordFailedCheck(string.Join(" ", errors));
        }

        await configurationRepository.UpdateAsync(configuration, cancellationToken);
        return new TodoistProcessingResult(imported, ignored, alreadyProcessed, errors);
    }

    private static bool IsIgnored(TodoistTaskSnapshot task, TodoistInputConfiguration configuration) =>
        configuration.IsProjectIgnored(task.ProjectId) || task.Labels.Any(configuration.IsLabelIgnored);

    private static TodoistTaskInboundEvent CreateEvent(
        TodoistTaskSnapshot task,
        DateTimeOffset dueAt,
        string contextKey,
        TodoistInputConfiguration configuration,
        DateTimeOffset now) => new()
        {
            TaskId = task.TaskId,
            Content = task.Content,
            Description = task.Description,
            ProjectId = task.ProjectId,
            ProjectName = task.ProjectName,
            Labels = task.Labels.ToList(),
            Priority = task.Priority,
            Due = task.Due,
            DueAt = dueAt,
            DeadlineDate = task.DeadlineDate,
            Url = task.Url,
            ContextKey = contextKey,
            SourceGuidance = configuration.IntakeGuidance,
            ReceivedAt = now,
            Status = InboundEventStatus.Pending,
            Explanation = "Waiting for intake classification."
        };

    // Earlier occurrences of the same task let intake append a reschedule to its existing work item.
    private static List<InboundEventContextItem> BuildContext(IEnumerable<TodoistTaskInboundEvent> previousEvents) =>
        previousEvents
            .OrderByDescending(item => item.ReceivedAt)
            .Take(MaximumPriorOccurrences)
            .OrderBy(item => item.ReceivedAt)
            .Select(item => new InboundEventContextItem
            {
                SourceItemId = item.ExternalId,
                Content = $"Earlier occurrence of this task, due {item.Due.Text ?? item.Due.Date} ({item.DueAt:O}): {item.Content}",
                OccurredAt = item.ReceivedAt,
                PreviouslyProcessed = true,
                InboundEventId = item.Id,
                WorkItemIds = item.Outcomes
                    .Select(outcome => outcome.WorkItemId)
                    .Distinct(StringComparer.Ordinal)
                    .ToList()
            })
            .ToList();
}

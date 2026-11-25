using System.Security.Cryptography;
using System.Text;
using MediatR;
using Samwise.Domain.Inbox;
using Samwise.Domain.Tasks;
using Samwise.Domain.WorkItems;
using Samwise.TaskSources.Todoist.Configuration;
using Samwise.TaskSources.Todoist.Legacy;
using Samwise.TaskSources.Todoist.Synchronization;
using Samwise.Logic.WorkItems.Services;
using Shared.Infrastructure.Persistence.Marten.UnitOfWorks;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.TaskSources.Todoist.Synchronization;

internal sealed class TodoistTaskSyncService(IRepository<TodoistTaskSourceConfiguration> configurations,
    IRepository<AgentTask> tasks, IRepository<WorkItem> workItems, IReadOnlyRepository<InboundEvent> inputs,
    ITodoistTaskReader reader, ITodoistSynchronizationLock processingLock, IWorkItemEventPublisher events,
    IPublisher publisher, TimeProvider clock) : ITodoistTaskSyncService
{
    public async Task<TodoistSynchronizationResult> SynchronizeAsync(string? externalTaskId, CancellationToken ct)
    {
        using var lease = await processingLock.EnterAsync(ct);
        var config = await configurations.GetRequiredByIdAsync(TodoistTaskSourceConfiguration.DocumentId, ct);
        if (!config.IsConfigured) return new(0, 0, 0, ["Todoist is not connected."]);
        var now = clock.GetUtcNow();
        var imported = 0;
        var ignored = 0;
        var unchanged = 0;
        var errors = new List<string>();
        try
        {
            var states = new Dictionary<string, TodoistTaskState>(StringComparer.Ordinal);
            if (externalTaskId is { } requestedId) states[requestedId] = await reader.ReadTaskStateAsync(requestedId, ct);
            else
            {
                var result = await reader.ReadDueTasksAsync(ct);
                foreach (var snapshot in result.Tasks)
                    states[snapshot.TaskId] = new("active", snapshot, result.AccountTimeZone, result.AccountUserId);
                foreach (var linked in await tasks.ListAsync(new ActiveTodoistTasksSpecification(), ct))
                {
                    var externalId = linked.ExternalSource!.TaskId;
                    if (!states.ContainsKey(externalId))
                    {
                        try { states[externalId] = await reader.ReadTaskStateAsync(externalId, ct); }
                        catch (Exception exception) when (exception is not OperationCanceledException)
                        { errors.Add($"Todoist task {externalId}: {exception.Message}"); }
                    }
                }
            }
            foreach (var (id, state) in states)
            {
                try
                {
                    var result = await SyncOneAsync(id, state, config, now, ct);
                    if (result == "imported") imported++;
                    else if (result == "ignored") ignored++;
                    else unchanged++;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                { errors.Add($"Todoist task {id}: {exception.Message}"); }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { errors.Add(exception.Message); }
        if (errors.Count == 0) config.RecordSuccessfulCheck(now);
        else config.RecordFailedCheck(string.Join(" ", errors));
        await configurations.UpdateAsync(config, ct);
        return new(imported, ignored, unchanged, errors);
    }

    private async Task<string> SyncOneAsync(string id, TodoistTaskState state, TodoistTaskSourceConfiguration config,
        DateTimeOffset now, CancellationToken ct)
    {
        var existing = await tasks.GetBySpecAsync(new LatestTodoistTaskSpecification(id), ct);
        // Every generation derives from the latest one, so its owner removal excludes all future occurrences.
        if (existing?.ExternalSource?.State == TodoistTaskRetirement.DismissedState) return "ignored";
        var snapshot = state.Task;
        var due = snapshot?.Due.ResolveDueAt(state.AccountTimeZone);
        var selected = snapshot is not null && config.IsSelected(snapshot.ProjectId, snapshot.Labels) &&
            (snapshot.ResponsibleUserId is null || snapshot.ResponsibleUserId == state.AccountUserId);
        var sourceState = state.State != "active" ? state.State : !selected ? "out_of_scope" : due is null ? "undated" : "active";
        if (sourceState != "active")
        {
            if (existing is not null && existing.ExternalSource!.IsActive)
                await RetireAsync(existing, sourceState, now, ct);
            return "ignored";
        }
        var comments = await reader.ReadCommentsAsync(id, ct);
        var instructions = BuildInstructions(snapshot!, comments, config.IntakeGuidance);
        var contentFingerprint = ContentFingerprint(snapshot!.Content, snapshot.Description, config.IntakeGuidance, comments);
        var knownCount = existing?.ExternalSource?.CompletedCount;
        var effectiveCount = snapshot.CompletedCount is { } observed ? Math.Max(observed, knownCount ?? observed) : knownCount;
        var fingerprint = Hash($"{contentFingerprint}\n{due:O}\n{snapshot.DeadlineDate}\n{snapshot.Priority}\n{snapshot.Url}\n{effectiveCount}\n{snapshot.Due.IsRecurring}");
        var sourceChanged = existing?.ExternalSource?.Fingerprint != fingerprint;
        if (existing?.ExternalSource is { IsActive: true } oldSource && oldSource.Fingerprint == fingerprint)
            return "unchanged"; // Do not overwrite an agent's next check or checklist on every poll.

        var newOccurrence = existing?.ExternalSource is { IsRecurring: true } prior && snapshot.Due.IsRecurring &&
            ((prior.CompletedCount is { } oldCount && snapshot.CompletedCount is { } count && count > oldCount) ||
                ((prior.CompletedCount is null || snapshot.CompletedCount is null) &&
                    prior.State == "completed" && due > prior.DueAt));
        if (newOccurrence) await RetireAsync(existing!, "completed", now, ct);
        var isNew = existing is null || newOccurrence;
        var task = isNew ? new AgentTask { Id = $"todoist:{id}:{(existing?.ExternalSource?.Generation ?? -1) + 1}" } : existing!;
        if (newOccurrence)
            task.OriginWorkItemId = existing!.WorkItemIds.LastOrDefault() ?? existing.OriginWorkItemId;
        var nextRun = due!.Value > now ? due.Value : now;
        if (!isNew && task.NextRunAt is { } savedCheck)
        {
            var source = task.ExternalSource!;
            var initialOverdueCheck = task.LastRunAt is null && savedCheck == source.VerifiedAt && source.DueAt <= source.VerifiedAt;
            if (savedCheck != source.DueAt && !initialOverdueCheck)
                nextRun = savedCheck; // A custom Samwise check is independent of the source due date.
        }
        WorkItem? pending = null;
        if (isNew && existing is null)
        {
            // Preserve a live pre-migration work item instead of creating a second intervention.
            var legacyEvents = await inputs.ListAsync(new TodoistTaskEventsSpecification(TodoistTaskInboundEvent.CreateContextKey(id)), ct);
            var legacyIds = legacyEvents.SelectMany(item => item.Outcomes).Select(item => item.WorkItemId).Distinct().ToList();
            var history = await workItems.GetByIdsAsync(legacyIds, ct);
            var active = history.Where(item => item.Status is not (WorkItemStatus.Completed or WorkItemStatus.Discarded)).ToList();
            if (active.Count > 1) throw new InvalidOperationException("Multiple live legacy work items need reconciliation before import.");
            task.WorkItemIds = history.OrderBy(item => item.CreatedAt).Select(item => item.Id).ToList();
            pending = active.SingleOrDefault();
            if (pending is not null)
            {
                if (pending.AgentTaskId is { } linkedTaskId && linkedTaskId != task.Id)
                    throw new InvalidOperationException("The legacy work item already belongs to another task; reconcile its source links before import.");
                task.WorkItemIds.Remove(pending.Id);
                task.WorkItemIds.Add(pending.Id); // The adopted live intervention always governs the next run.
                pending.AgentTaskId = task.Id;
                var latest = legacyEvents.OfType<TodoistTaskInboundEvent>()
                    .Where(item => item.Outcomes.Any(outcome => outcome.WorkItemId == pending.Id))
                    .OrderByDescending(item => item.ReceivedAt).FirstOrDefault();
                if (latest is null || latest.DueAt != due || latest.DeadlineDate != snapshot.DeadlineDate ||
                    latest.Priority != snapshot.Priority || latest.Due.IsRecurring != snapshot.Due.IsRecurring ||
                    ContentFingerprint(latest.Content, latest.Description, latest.SourceGuidance,
                        latest.Comments.Select(item => new TodoistCommentSnapshot(item.CommentId, item.PostedAt, item.Content))) != contentFingerprint)
                    TodoistTaskRetirement.InvalidatePendingWork(pending, "Todoist source changed during migration.", now);
            }
        }
        else if (!isNew && task.WorkItemIds.LastOrDefault() is { } last)
        {
            pending = await workItems.GetRequiredByIdAsync(last, ct);
            if (sourceChanged && pending.Status is not (WorkItemStatus.Completed or WorkItemStatus.Discarded))
                TodoistTaskRetirement.InvalidatePendingWork(pending, "Todoist title, instructions, comments or due date changed.", now);
        }
        var liveRun = pending is not null && pending.Status is not (WorkItemStatus.Completed or WorkItemStatus.Discarded);
        var checklist = isNew ? new List<TaskChecklistItem> { new() { Text = Limit(snapshot.Content, 2_000) } }
            : task.Checklist.Select(item => item.Copy()).ToList();
        if (!isNew && task.CompletedAt is not null && task.ExternalSource!.ContentFingerprint != contentFingerprint)
            checklist.Add(new TaskChecklistItem { Text = TaskErrors.ChangedSource });
        task.Update(Limit(snapshot.Content, 200), instructions, checklist,
            checklist.All(item => item.IsCompleted) || liveRun ? null : nextRun, now);
        task.ExternalSource = new ExternalTaskReference
        {
            Provider = "Todoist", TaskId = id, Url = snapshot.Url, DueAt = due, IsRecurring = snapshot.Due.IsRecurring,
            CompletedCount = effectiveCount,
            Generation = isNew ? (existing?.ExternalSource?.Generation ?? -1) + 1 : task.ExternalSource!.Generation,
            Fingerprint = fingerprint, ContentFingerprint = contentFingerprint, VerifiedAt = now, IsActive = true, State = "active"
        };
        await PersistAsync(task, isNew, pending, ct);
        if (pending is not null) await events.PublishAsync(pending);
        await publisher.Publish(new AgentTaskChanged(task.Id), ct);
        return isNew ? "imported" : "updated";
    }

    private async Task RetireAsync(AgentTask task, string state, DateTimeOffset now, CancellationToken ct)
    {
        var pending = task.WorkItemIds.LastOrDefault() is { } id ? await workItems.GetRequiredByIdAsync(id, ct) : null;
        TodoistTaskRetirement.Retire(task, pending, state, now);
        await PersistAsync(task, false, pending, ct);
        if (pending is not null) await events.PublishAsync(pending);
        await publisher.Publish(new AgentTaskChanged(task.Id), ct);
    }

    private async Task PersistAsync(AgentTask task, bool isNew, WorkItem? pending, CancellationToken ct)
    {
        await using var unit = new UnitOfWorkProvider();
        if (isNew) await tasks.AddAsync(task, ct); else await tasks.UpdateAsync(task, ct);
        if (pending is not null) await workItems.UpdateAsync(pending, ct);
        await unit.CommitAsync();
    }

    private static string BuildInstructions(TodoistTaskSnapshot task, IReadOnlyList<TodoistCommentSnapshot> comments, string guidance) =>
        $"Todoist task: {task.TaskId}\nSource: {task.Url}\nDue: {task.Due.Date}\nDeadline: {task.DeadlineDate:yyyy-MM-dd}\nPriority: {task.Priority}\n{Limit(task.Content, 500)}\n\n" +
        Limit(task.Description, 10_000) + "\n\nSource guidance:\n" + Limit(guidance, 2_000) +
        "\n\nRecent source comments (excerpts; read the exact source before acting when material):\n" +
        string.Join("\n", comments.OrderBy(item => item.PostedAt).TakeLast(10).Select(item => $"[{item.CommentId}] {Limit(item.Content, 500)}"));

    private static string Limit(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static string ContentFingerprint(string content, string description, string guidance,
        IEnumerable<TodoistCommentSnapshot> comments) => Hash(System.Text.Json.JsonSerializer.Serialize(new
        {
            Content = content, Description = description, Guidance = guidance,
            Comments = comments.OrderBy(item => item.CommentId, StringComparer.Ordinal).ToArray()
        }));
}

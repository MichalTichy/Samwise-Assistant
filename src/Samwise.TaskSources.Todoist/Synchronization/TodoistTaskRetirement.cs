using Samwise.Domain.Tasks;
using Samwise.Domain.WorkItems;

namespace Samwise.TaskSources.Todoist.Synchronization;

/// <summary>Rules shared by synchronization and owner removal for stopping work on a Todoist-linked task.</summary>
internal static class TodoistTaskRetirement
{
    public const string DismissedState = "dismissed";
    public const string RestoredState = "restored";

    /// <summary>Deactivates the task and withdraws its latest unexecuted intervention.</summary>
    public static void Retire(AgentTask task, WorkItem? latestWorkItem, string state, DateTimeOffset now)
    {
        if (latestWorkItem is not null)
        {
            var reason = state == DismissedState
                ? "Todoist task was removed from Samwise; no further automatic work is delegated."
                : $"Todoist source is {state}; no further automatic work is delegated.";
            InvalidatePendingWork(latestWorkItem, reason, now, needsAttention: state == "unavailable");
        }

        task.ExternalSource!.IsActive = false;
        task.ExternalSource.State = state;
        task.ExternalSource.VerifiedAt = now;
        task.NextRunAt = null;
        task.UpdatedAt = now;
    }

    /// <summary>Discards unexecuted work; a started or uncertain execution is never interrupted.</summary>
    public static void InvalidatePendingWork(WorkItem item, string reason, DateTimeOffset now, bool needsAttention = false)
    {
        if (item.Status is WorkItemStatus.Completed or WorkItemStatus.Discarded) return;
        if (item.Status is WorkItemStatus.Executing or WorkItemStatus.OutcomeUnknown) return;
        if (needsAttention)
        {
            item.AddContext(reason, now, CommentAuthorKind.System);
            item.MarkPlanningFailed(now, reason);
            item.AddExecutionUpdate(reason, now);
        }
        else item.Discard(now, "Todoist source", reason);
    }
}

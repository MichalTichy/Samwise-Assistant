using System.Text;
using Samwise.Domain.Inbox;

namespace Samwise.Inputs.Todoist.InboundEvents;

public sealed class TodoistTaskInboundEvent : InboundEvent
{
    public const string EventSource = "Todoist";

    public string TaskId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string? ProjectName { get; set; }
    public List<string> Labels { get; set; } = [];
    public int Priority { get; set; } = 1;
    public required TodoistDue Due { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public string? DeadlineDate { get; set; }
    public string? Url { get; set; }
    public List<TodoistTaskComment> Comments { get; set; } = [];

    public override string Source => EventSource;

    // One event per due occurrence: a reschedule or the next recurrence is a new input.
    public override string ExternalId => $"task:{TaskId}:due:{Due.Date.Trim()}";

    public static string CreateContextKey(string taskId) => $"todoist:task:{taskId}";

    public override string GetIntakeText()
    {
        var text = new StringBuilder()
            .AppendLine("A Todoist task is due soon.")
            .Append("Task: ").AppendLine(Content.Trim())
            .Append("Todoist task ID: ").AppendLine(TaskId);
        if (!string.IsNullOrWhiteSpace(Url))
        {
            text.Append("Task URL: ").AppendLine(Url);
        }

        text.Append("Project: ").AppendLine(ProjectName ?? ProjectId);
        if (Labels.Count > 0)
        {
            text.Append("Labels: ").AppendLine(string.Join(", ", Labels));
        }

        text.Append("Priority: p").AppendLine((5 - Math.Clamp(Priority, 1, 4)).ToString());
        text.Append("Due: ");
        if (!string.IsNullOrWhiteSpace(Due.Text))
        {
            text.Append(Due.Text.Trim()).Append(" — ");
        }

        text.Append(Due.IsAllDay ? Due.Date.Trim() : DueAt.ToString("O"));
        if (Due.IsRecurring)
        {
            text.Append(" (recurring)");
        }

        text.AppendLine();
        if (!string.IsNullOrWhiteSpace(DeadlineDate))
        {
            text.Append("Deadline: ").AppendLine(DeadlineDate);
        }

        if (!string.IsNullOrWhiteSpace(Description))
        {
            text.AppendLine().AppendLine("Description:").AppendLine(Description.Trim());
        }

        if (Comments.Count > 0)
        {
            text.AppendLine().AppendLine("Comments:");
            foreach (var comment in Comments.OrderBy(comment => comment.PostedAt))
            {
                text.AppendLine()
                    .Append('[').Append(comment.PostedAt?.ToString("O") ?? "unknown time").AppendLine("]")
                    .AppendLine(comment.Content.Trim());
            }
        }

        return text.ToString().Trim();
    }
}

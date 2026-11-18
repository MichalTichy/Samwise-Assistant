using JasperFx;
using Shared.Infrastructure.Persistence;

namespace Samwise.Domain.Tasks;

public sealed class AgentTask : IItemWithId, IRevisioned
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public int Version { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public List<TaskChecklistItem> Checklist { get; set; } = [];
    public DateTimeOffset? NextRunAt { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? RoutineId { get; set; }
    public DateTimeOffset? OccurrenceAt { get; set; }
    public string? OriginWorkItemId { get; set; }
    public ExternalTaskReference? ExternalSource { get; set; }
    public List<string> WorkItemIds { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; }

    public void Update(string title, string instructions, IEnumerable<TaskChecklistItem> checklist,
        DateTimeOffset? nextRunAt, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(instructions);
        var items = checklist.Select(item => item.Copy()).ToList();
        if (title.Length > 200 || instructions.Length > 20_000 || items.Count is < 1 or > 100 ||
            items.Any(item => string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Text) ||
                item.Text.Length > 2_000 || item.Evidence?.Length > 4_000 ||
                (item.IsCompleted && string.IsNullOrWhiteSpace(item.Evidence))) ||
            items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != items.Count)
            throw new ArgumentException(Samwise.Domain.Tasks.TaskErrors.InvalidTask);
        if (items.All(item => item.IsCompleted) && nextRunAt is not null)
            throw new ArgumentException(Samwise.Domain.Tasks.TaskErrors.CompletedWithNext);
        Title = title.Trim();
        Instructions = instructions.Trim();
        Checklist = items;
        NextRunAt = nextRunAt?.ToUniversalTime();
        OccurrenceAt ??= NextRunAt;
        CompletedAt = items.All(item => item.IsCompleted) ? CompletedAt ?? now : null;
        UpdatedAt = now;
    }

    public void StartRun(string workItemId, DateTimeOffset now)
    {
        if (CompletedAt is not null || NextRunAt is null || NextRunAt > now)
            throw new InvalidOperationException(Samwise.Domain.Tasks.TaskErrors.NotDue);
        WorkItemIds.Add(workItemId);
        LastRunAt = now;
        NextRunAt = null; // A new run must be deliberately scheduled, never retried blindly.
        UpdatedAt = now;
    }
}
